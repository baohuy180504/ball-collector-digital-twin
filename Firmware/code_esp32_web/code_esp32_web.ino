#include <Arduino.h>
#include <WiFi.h>
#include <WiFiUdp.h>
#include <Wire.h>
#include "bot_portal.h"          // Captive portal & Wi-Fi manager ("BallBot-XXXX")

// ======================== NETWORK CONFIGURATION ========================
// Default network settings (used on first flash or after factory reset)
const char* WIFI_SSID     = "YOUR_WIFI_SSID";            // Replace with your 2.4 GHz Wi-Fi SSID
const char* WIFI_PASSWORD = "YOUR_WIFI_PASSWORD";        // Replace with your Wi-Fi Password
const char* PC_IP_ADDR    = "172.18.40.138";     // Replace with your PC's IP address
const uint16_t PC_PORT    = 5005;                // Target UDP port on Python server
const uint16_t LOCAL_PORT = 5006;                // Local UDP listening port

WiFiUDP udp;

// ======================== PIN DEFINITIONS ========================
// Motor Left (Channel A on L298N)
#define PIN_ENA 25
#define PIN_IN1 26
#define PIN_IN2 27

// Motor Right (Channel B on L298N)
#define PIN_ENB 4
#define PIN_IN3 16
#define PIN_IN4 17

// Stepper Motor (28BYJ-48 via ULN2003 Driver)
#define PIN_STEP_IN1 13
#define PIN_STEP_IN2 14
#define PIN_STEP_IN3 23
#define PIN_STEP_IN4 5

// Encoders (AB Phase)
#define PIN_ENC_L_A 18
#define PIN_ENC_L_B 19
#define PIN_ENC_R_A 32
#define PIN_ENC_R_B 33

// ADC Battery Voltage Sense (GPIO 34, ADC1_CH6)
#define PIN_ADC_BAT 34

// I2C IMU (MPU6050)
#define PIN_I2C_SDA 21
#define PIN_I2C_SCL 22
#define MPU6050_ADDR 0x68

// ======================== PWM & STEPPER CONFIGURATION ========================
const int PWM_FREQ = 2000;       // 2 kHz PWM frequency for DC motors
const int PWM_RES  = 8;          // 8-bit resolution (0 - 255)

// Stepper speed control (1500 us per half-step ~= 9.8 RPM on 28BYJ-48)
const unsigned long STEP_INTERVAL_US = 1000;

// 8-step half-step commutation sequence for smooth rotation
const uint8_t STEPPER_SEQ[8][4] = {
  {1, 0, 0, 0},
  {1, 1, 0, 0},
  {0, 1, 0, 0},
  {0, 1, 1, 0},
  {0, 0, 1, 0},
  {0, 0, 1, 1},
  {0, 0, 0, 1},
  {1, 0, 0, 1}
};

// ======================== GLOBAL VARIABLES ========================
// Encoder tick counters
volatile long enc_left_ticks  = 0;
volatile long enc_right_ticks = 0;

// Stepper motor state variables
bool stepper_running = false;
uint8_t stepper_step_idx = 0;
unsigned long last_step_time_us = 0;

// IMU variables
float roll  = 0.0f;
float pitch = 0.0f;
float gz_deg = 0.0f;             // Calibrated yaw angular velocity in deg/s
float gz_bias_offset = 0.0f;     // Hardware zero-rate calibration bias
unsigned long prev_imu_time = 0;

// Filtered voltage variable
float filtered_voltage = 12.0f;

// Watchdog failsafe timer (for continuous DC drive commands)
unsigned long last_cmd_time = 0;
const unsigned long CMD_TIMEOUT_MS = 800;

// ======================== INTERRUPT ROUTINES ========================
void IRAM_ATTR isr_encoder_left() {
  // Read Channel B to determine rotation direction
  if (digitalRead(PIN_ENC_L_B) == HIGH) {
    enc_left_ticks++;
  } else {
    enc_left_ticks--;
  }
}

void IRAM_ATTR isr_encoder_right() {
  // Read Channel B to determine rotation direction
  if (digitalRead(PIN_ENC_R_B) == HIGH) {
    enc_right_ticks++;
  } else {
    enc_right_ticks--;
  }
}

// ======================== DC MOTOR FUNCTIONS ========================
void initMotors() {
  pinMode(PIN_IN1, OUTPUT);
  pinMode(PIN_IN2, OUTPUT);
  pinMode(PIN_IN3, OUTPUT);
  pinMode(PIN_IN4, OUTPUT);

  // Setup hardware PWM using ESP32 Core v3.x API directly on pins
  ledcAttach(PIN_ENA, PWM_FREQ, PWM_RES);
  ledcAttach(PIN_ENB, PWM_FREQ, PWM_RES);
}

void driveMotors(int pwm_l, int pwm_r) {
  pwm_l = constrain(pwm_l, -255, 255);
  pwm_r = constrain(pwm_r, -255, 255);

  // Left Motor Control
  if (pwm_l > 0) {
    digitalWrite(PIN_IN1, HIGH);
    digitalWrite(PIN_IN2, LOW);
    ledcWrite(PIN_ENA, pwm_l);
  } else if (pwm_l < 0) {
    digitalWrite(PIN_IN1, LOW);
    digitalWrite(PIN_IN2, HIGH);
    ledcWrite(PIN_ENA, -pwm_l);
  } else {
    digitalWrite(PIN_IN1, LOW);
    digitalWrite(PIN_IN2, LOW);
    ledcWrite(PIN_ENA, 0);
  }

  // Right Motor Control
  if (pwm_r > 0) {
    digitalWrite(PIN_IN3, HIGH);
    digitalWrite(PIN_IN4, LOW);
    ledcWrite(PIN_ENB, pwm_r);
  } else if (pwm_r < 0) {
    digitalWrite(PIN_IN3, LOW);
    digitalWrite(PIN_IN4, HIGH);
    ledcWrite(PIN_ENB, -pwm_r);
  } else {
    digitalWrite(PIN_IN3, LOW);
    digitalWrite(PIN_IN4, LOW);
    ledcWrite(PIN_ENB, 0);
  }
}

// ======================== STEPPER MOTOR FUNCTIONS ========================
void disableStepperCoils() {
  // De-energize all coils to prevent ULN2003 and stepper overheating when stopped
  digitalWrite(PIN_STEP_IN1, LOW);
  digitalWrite(PIN_STEP_IN2, LOW);
  digitalWrite(PIN_STEP_IN3, LOW);
  digitalWrite(PIN_STEP_IN4, LOW);
}

void initStepper() {
  pinMode(PIN_STEP_IN1, OUTPUT);
  pinMode(PIN_STEP_IN2, OUTPUT);
  pinMode(PIN_STEP_IN3, OUTPUT);
  pinMode(PIN_STEP_IN4, OUTPUT);
  disableStepperCoils();
}

void setStepperRun(bool run) {
  stepper_running = run;
  if (!stepper_running) {
    disableStepperCoils();
  }
}

void updateStepper() {
  // Do nothing if the stepper is in STOP state
  if (!stepper_running) return;

  // Non-blocking step timing using micros() so IMU and UDP tasks never stall
  unsigned long now_us = micros();
  if (now_us - last_step_time_us >= STEP_INTERVAL_US) {
    last_step_time_us = now_us;

    digitalWrite(PIN_STEP_IN1, STEPPER_SEQ[stepper_step_idx][0]);
    digitalWrite(PIN_STEP_IN2, STEPPER_SEQ[stepper_step_idx][1]);
    digitalWrite(PIN_STEP_IN3, STEPPER_SEQ[stepper_step_idx][2]);
    digitalWrite(PIN_STEP_IN4, STEPPER_SEQ[stepper_step_idx][3]);

    // Advance to next half-step (use (stepper_step_idx + 7) & 0x07 to reverse direction)
    stepper_step_idx = (stepper_step_idx + 1) & 0x07;
  }
}

// ======================== IMU FUNCTIONS ========================
void initMPU6050() {
  Wire.begin(PIN_I2C_SDA, PIN_I2C_SCL, 400000);
  Wire.beginTransmission(MPU6050_ADDR);
  Wire.write(0x6B); // Power Management 1 register
  Wire.write(0x00); // Wake up MPU6050
  Wire.endTransmission(true);

  // Calibrate Gyro Z zero-rate offset (collect 200 samples while robot is stationary)
  Serial.println("Calibrating Gyro Z offset... Keep robot still!");
  float sum_gz = 0.0f;
  for (int i = 0; i < 200; i++) {
    Wire.beginTransmission(MPU6050_ADDR);
    Wire.write(0x47); // GYRO_ZOUT_H
    Wire.endTransmission(false);
    Wire.requestFrom((uint8_t)MPU6050_ADDR, (size_t)2, true);
    if (Wire.available() >= 2) {
      int16_t raw_z = (Wire.read() << 8) | Wire.read();
      sum_gz += (float)raw_z / 131.0f;
    }
    delay(5);
  }
  gz_bias_offset = sum_gz / 200.0f;
  Serial.printf("Gyro Z bias calibrated: %.3f deg/s\n", gz_bias_offset);
}

void updateIMU() {
  Wire.beginTransmission(MPU6050_ADDR);
  Wire.write(0x3B); // Starting from ACCEL_XOUT_H
  Wire.endTransmission(false);
  Wire.requestFrom((uint8_t)MPU6050_ADDR, (size_t)14, true);

  if (Wire.available() < 14) return;

  int16_t ax_raw = (Wire.read() << 8) | Wire.read();
  int16_t ay_raw = (Wire.read() << 8) | Wire.read();
  int16_t az_raw = (Wire.read() << 8) | Wire.read();
  Wire.read(); Wire.read(); // Skip temperature bytes
  int16_t gx_raw = (Wire.read() << 8) | Wire.read();
  int16_t gy_raw = (Wire.read() << 8) | Wire.read();
  int16_t gz_raw = (Wire.read() << 8) | Wire.read();

  // Convert raw values to physical units
  float ax = (float)ax_raw / 16384.0f;
  float ay = (float)ay_raw / 16384.0f;
  float az = (float)az_raw / 16384.0f;
  float gx = (float)gx_raw / 131.0f;
  float gy = (float)gy_raw / 131.0f;
  gz_deg   = ((float)gz_raw / 131.0f) - gz_bias_offset;

  // Calculate delta time
  unsigned long current_time = micros();
  float dt = (current_time - prev_imu_time) / 1000000.0f;
  prev_imu_time = current_time;
  if (dt <= 0.0f || dt > 0.1f) dt = 0.01f;

  // Accelerometer tilt calculation
  float roll_acc  = atan2(ay, az) * 180.0f / PI;
  float pitch_acc = atan2(-ax, sqrt(ay * ay + az * az)) * 180.0f / PI;

  // Complementary filter for Roll and Pitch
  const float alpha = 0.96f;
  roll  = alpha * (roll + gx * dt) + (1.0f - alpha) * roll_acc;
  pitch = alpha * (pitch + gy * dt) + (1.0f - alpha) * pitch_acc;
}

// ======================== BATTERY ADC READING ========================
float readBatteryVoltage() {
  // Read factory eFuse-calibrated millivolts directly from ESP32
  uint32_t mv_pin = analogReadMilliVolts(PIN_ADC_BAT);
  float v_pin = mv_pin / 1000.0f;

  // Scale back with 5:1 hardware voltage divider
  float v_bat = v_pin * 5.0f;

  // Exponential moving average filter
  filtered_voltage = 0.90f * filtered_voltage + 0.10f * v_bat;
  return filtered_voltage;
}

// ======================== UDP PACKET HANDLING ========================
void sendTelemetry() {
  noInterrupts();
  long enc_l = enc_left_ticks;
  long enc_r = enc_right_ticks;
  interrupts();

  float v_bat = readBatteryVoltage();

  // Transmit: Left Enc, Right Enc, Roll, Pitch, Gyro Z rate, Voltage, Stepper state
  char buffer[160];
  snprintf(buffer, sizeof(buffer),
           "{\"l\":%ld,\"r\":%ld,\"roll\":%.2f,\"pitch\":%.2f,\"gz\":%.3f,\"v\":%.2f,\"step\":%d}",
           enc_l, enc_r, roll, pitch, gz_deg, v_bat, stepper_running ? 1 : 0);

  BotNet::Manager& net = BotNet::manager();
  udp.beginPacket(net.pcTarget(), net.pcPort());
  udp.write((const uint8_t*)buffer, strlen(buffer));
  udp.endPacket();
  net.noteTelemetry(v_bat);
}

void processIncomingCommand() {
  int packet_size = udp.parsePacket();
  if (packet_size > 0) {
    char packet_buf[64];
    int len = udp.read(packet_buf, sizeof(packet_buf) - 1);
    if (len > 0) {
      packet_buf[len] = '\0';

      // Strip trailing whitespace or newline characters from web/server packets
      while (len > 0 && (packet_buf[len - 1] == '\n' || packet_buf[len - 1] == '\r' || packet_buf[len - 1] == ' ')) {
        packet_buf[--len] = '\0';
      }

      // Handle RUN / STOP commands sent from the web interface
      if (strcasecmp(packet_buf, "RUN") == 0 ||
          strcasecmp(packet_buf, "cmd:RUN") == 0 ||
          strcasecmp(packet_buf, "step:RUN") == 0) {
        setStepperRun(true);
        BotNet::manager().notePcCommand(udp.remoteIP());
        return;
      }

      if (strcasecmp(packet_buf, "STOP") == 0 ||
          strcasecmp(packet_buf, "cmd:STOP") == 0 ||
          strcasecmp(packet_buf, "step:STOP") == 0) {
        setStepperRun(false);
        BotNet::manager().notePcCommand(udp.remoteIP());
        return;
      }

      // Handle continuous DC motor PWM commands
      int cmd_l = 0, cmd_r = 0;
      if (sscanf(packet_buf, "cmd:%d,%d", &cmd_l, &cmd_r) == 2) {
        driveMotors(cmd_l, cmd_r);
        last_cmd_time = millis();
        BotNet::manager().notePcCommand(udp.remoteIP());
      }
    }
  }
}

// ======================== SETUP & MAIN LOOP ========================
void setup() {
  Serial.begin(115200);

  initMotors();
  driveMotors(0, 0);

  initStepper();

  // Encoder interrupts
  pinMode(PIN_ENC_L_A, INPUT_PULLUP);
  pinMode(PIN_ENC_L_B, INPUT_PULLUP);
  pinMode(PIN_ENC_R_A, INPUT_PULLUP);
  pinMode(PIN_ENC_R_B, INPUT_PULLUP);
  attachInterrupt(digitalPinToInterrupt(PIN_ENC_L_A), isr_encoder_left, RISING);
  attachInterrupt(digitalPinToInterrupt(PIN_ENC_R_A), isr_encoder_right, RISING);

  // ADC setup
  analogSetAttenuation(ADC_11db);
  pinMode(PIN_ADC_BAT, INPUT);

  // IMU setup
  initMPU6050();
  prev_imu_time = micros();

  // Initialize non-blocking Wi-Fi & captive portal
  BotNet::Options o;
  o.defaultSsid   = WIFI_SSID;
  o.defaultPass   = WIFI_PASSWORD;
  o.defaultPcIp   = PC_IP_ADDR;
  o.defaultPcPort = PC_PORT;
  BotNet::manager().begin(o);

  udp.begin(LOCAL_PORT);
  last_cmd_time = millis();
}

void loop() {
  // Update IMU at 100 Hz (every 10 ms)
  static unsigned long last_imu_time = 0;
  if (millis() - last_imu_time >= 10) {
    last_imu_time = millis();
    updateIMU();
  }

  // Parse incoming UDP commands (DC motors + Stepper RUN/STOP)
  processIncomingCommand();

  // Step the ULN2003 motor smoothly without blocking loop()
  updateStepper();

  // Failsafe auto-stop for DC motors if continuous drive stream drops
  if (millis() - last_cmd_time > CMD_TIMEOUT_MS) {
    driveMotors(0, 0);
  }

  // Stream telemetry at 30 Hz (every 33 ms)
  static unsigned long last_telemetry_time = 0;
  if (millis() - last_telemetry_time >= 33) {
    last_telemetry_time = millis();
    sendTelemetry();
  }

  BotNet::manager().loop();
}