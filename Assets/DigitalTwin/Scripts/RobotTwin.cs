using System.Collections.Generic;
using UnityEngine;

namespace DigitalTwin
{
    /// <summary>
    /// Gan vao goc model robot (GameObject chua base_link).
    /// Dat pose theo du lieu camera + IMU, quay banh va cang banh tu xoay cho giong that.
    /// Robot KHONG chay bang vat ly: no chi di theo du lieu that.
    /// </summary>
    public class RobotTwin : MonoBehaviour
    {
        public TwinUdpReceiver receiver;

        [Header("Lam muot (cang lon cang bam sat, cang nho cang muot)")]
        public float positionSharpness = 15f;
        public float rotationSharpness = 15f;

        [Header("IMU")]
        public bool applyRollPitch = true;
        [Tooltip("Doi thanh -1 neu robot tren Unity nghieng nguoc chieu xe that")]
        public float rollSign = 1f;
        public float pitchSign = 1f;
        public float tiltAlarmDeg = 15f;
        public Color alarmColor = new Color(1f, 0.2f, 0.2f);

        [Header("Dong hoc (phai khop URDF va firmware)")]
        public float wheelRadius = 0.0325f;
        public float wheelTrack = 0.45f;
        public float casterWheelRadius = 0.0125f;

        [Header("Ten link trong URDF")]
        public string leftWheelName = "left_wheel_link";
        public string rightWheelName = "right_wheel_link";
        public string[] casterForkNames = { "left_caster_fork", "right_caster_fork" };
        public string[] casterWheelNames = { "left_caster_wheel", "right_caster_wheel" };

        public RobotState State { get; private set; }
        public bool HasPose { get; private set; }
        public bool TiltAlarm { get; private set; }
        public float LinearVel { get; private set; }   // m/s
        public float AngularVel { get; private set; }  // rad/s, duong = quay trai

        class Caster
        {
            public Transform fork, wheel;
            public Quaternion fork0, wheel0;
            public Vector2 pos;      // vi tri truc cang trong he robot (ROS), m
            public float swivelDeg, spin;
        }

        static readonly Vector3 RosY = new Vector3(0f, 1f, 0f);
        static readonly Vector3 RosZ = new Vector3(0f, 0f, 1f);

        Transform lw, rw;
        Quaternion lw0, rw0;
        float lwAngle, rwAngle;
        Caster[] casters = new Caster[0];
        Vector3 targetPos;
        Quaternion targetRot = Quaternion.identity;
        int lastSeq = -1;
        Vector3 prevPos;
        float prevYawDeg;
        bool alarmShown;
        Renderer[] renderers;
        MaterialPropertyBlock mpb;

        void Awake()
        {
            if (receiver == null) receiver = FindFirstObjectByType<TwinUdpReceiver>();
            DisablePhysics();

            lw = TwinUtil.FindDeep(transform, leftWheelName);
            rw = TwinUtil.FindDeep(transform, rightWheelName);
            if (lw != null) lw0 = lw.localRotation;
            if (rw != null) rw0 = rw.localRotation;

            var list = new List<Caster>();
            for (int i = 0; i < casterForkNames.Length; i++)
            {
                var f = TwinUtil.FindDeep(transform, casterForkNames[i]);
                if (f == null) continue;
                var c = new Caster { fork = f, fork0 = f.localRotation };
                if (i < casterWheelNames.Length)
                {
                    c.wheel = TwinUtil.FindDeep(transform, casterWheelNames[i]);
                    if (c.wheel != null) c.wheel0 = c.wheel.localRotation;
                }
                Vector3 ros = RosUnity.ToRos(transform.InverseTransformPoint(f.position));
                c.pos = new Vector2(ros.x, ros.y);
                list.Add(c);
            }
            casters = list.ToArray();

            renderers = GetComponentsInChildren<Renderer>();
            mpb = new MaterialPropertyBlock();
            prevPos = transform.position;
            prevYawDeg = transform.eulerAngles.y;
        }

        // Neu model duoc import bang URDF Importer thi tat vat ly de no khong roi / tu chay.
        void DisablePhysics()
        {
            foreach (var mb in GetComponentsInChildren<MonoBehaviour>(true))
                if (mb != this && mb.GetType().Name == "Controller") mb.enabled = false;
            foreach (var ab in GetComponentsInChildren<ArticulationBody>(true))
            {
                if (ab.isRoot) ab.immovable = true;
                Component c = ab;
                if (c is Behaviour b) b.enabled = false;
            }
            foreach (var rb in GetComponentsInChildren<Rigidbody>(true)) rb.isKinematic = true;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            var msg = receiver != null ? receiver.Latest : null;
            if (msg != null && receiver.Sequence != lastSeq)
            {
                lastSeq = receiver.Sequence;
                ApplyState(msg.robot);
            }

            if (HasPose)
            {
                transform.position = Vector3.Lerp(transform.position, targetPos, 1f - Mathf.Exp(-positionSharpness * dt));
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, 1f - Mathf.Exp(-rotationSharpness * dt));
            }

            UpdateVelocities(dt);
            AnimateWheels(dt);
            UpdateAlarmTint();
        }

        void ApplyState(RobotState r)
        {
            State = r;
            if (r == null || !r.valid) return;   // mat ma ArUco: giu pose cu

            float roll = applyRollPitch ? r.roll * rollSign : 0f;
            float pitch = applyRollPitch ? r.pitch * pitchSign : 0f;
            targetPos = RosUnity.ToUnity(r.x * 0.001f, r.y * 0.001f, 0f);
            targetRot = RosUnity.RpyToUnity(roll * Mathf.Deg2Rad, pitch * Mathf.Deg2Rad, r.yaw * Mathf.Deg2Rad);
            TiltAlarm = r.tilt_alarm || Mathf.Abs(r.roll) > tiltAlarmDeg || Mathf.Abs(r.pitch) > tiltAlarmDeg;

            if (!HasPose)
            {
                transform.SetPositionAndRotation(targetPos, targetRot);
                prevPos = targetPos;
                prevYawDeg = transform.eulerAngles.y;
                HasPose = true;
            }
        }

        void UpdateVelocities(float dt)
        {
            // Uoc luong v, w tu chuyen dong cua model (dung khi khong co toc do encoder)
            Vector3 local = transform.InverseTransformDirection(transform.position - prevPos);
            float vEst = local.z / dt;
            float yawU = transform.eulerAngles.y;
            float wEst = -Mathf.DeltaAngle(prevYawDeg, yawU) * Mathf.Deg2Rad / dt;
            prevPos = transform.position;
            prevYawDeg = yawU;

            float v, w;
            if (State != null && (Mathf.Abs(State.wl) > 1e-4f || Mathf.Abs(State.wr) > 1e-4f))
            {
                v = wheelRadius * (State.wl + State.wr) * 0.5f;
                w = wheelRadius * (State.wr - State.wl) / wheelTrack;
            }
            else
            {
                v = vEst;
                w = wEst;
            }
            LinearVel = Mathf.Lerp(LinearVel, v, 0.3f);
            AngularVel = Mathf.Lerp(AngularVel, w, 0.3f);
        }

        void AnimateWheels(float dt)
        {
            float v = LinearVel, w = AngularVel;
            float wl = (v - w * wheelTrack * 0.5f) / wheelRadius;
            float wr = (v + w * wheelTrack * 0.5f) / wheelRadius;
            lwAngle += wl * dt;
            rwAngle += wr * dt;
            if (lw != null) lw.localRotation = lw0 * RosUnity.AxisAngleToUnity(RosY, lwAngle);
            if (rw != null) rw.localRotation = rw0 * RosUnity.AxisAngleToUnity(RosY, rwAngle);

            foreach (var c in casters)
            {
                // van toc tai truc cang (he robot): v_c = v + w x r
                float vx = v - w * c.pos.y;
                float vy = w * c.pos.x;
                float sp = Mathf.Sqrt(vx * vx + vy * vy);
                if (sp > 0.01f)
                {
                    float heading = Mathf.Atan2(vy, vx) * Mathf.Rad2Deg;
                    c.swivelDeg = Mathf.MoveTowardsAngle(c.swivelDeg, heading, 720f * dt);
                }
                c.spin += sp / casterWheelRadius * dt;
                c.fork.localRotation = c.fork0 * RosUnity.AxisAngleToUnity(RosZ, c.swivelDeg * Mathf.Deg2Rad);
                if (c.wheel != null) c.wheel.localRotation = c.wheel0 * RosUnity.AxisAngleToUnity(RosY, c.spin);
            }
        }

        void UpdateAlarmTint()
        {
            if (TiltAlarm == alarmShown) return;
            alarmShown = TiltAlarm;
            foreach (var r in renderers)
            {
                if (r == null) continue;
                if (alarmShown)
                {
                    mpb.Clear();
                    mpb.SetColor("_BaseColor", alarmColor);
                    mpb.SetColor("_Color", alarmColor);
                    r.SetPropertyBlock(mpb);
                }
                else
                {
                    r.SetPropertyBlock(null);
                }
            }
        }
    }
}
