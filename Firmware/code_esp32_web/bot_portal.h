// ============================================================================
//  bot_portal.h - WiFi setup portal cho robot gom bong (ESP32 core 3.x)
//
//  * ESP32 luon PHAT WiFi "BallBot-XXXX" (XXXX = cuoi MAC) va dong thoi KET NOI WiFi da luu.
//  * Dien thoai noi vao BallBot-XXXX -> trang cau hinh tu bat (captive portal) / http://192.168.4.1
//  * Chi sua duoc cau hinh khi noi qua WiFi cua ESP32 (AP); tu mang LAN bi tu choi.
//  * Khi co dien thoai dang noi vao AP, ESP32 KHONG di tim router (tim router lam AP doi kenh
//    -> dien thoai bi vang ra). Chi thu khi bam "Save & connect" hoac khi khong ai noi.
//  * Giu nut BOOT 5 s: ve cau hinh mac dinh.
// ============================================================================
#pragma once
#include <Arduino.h>
#include <WiFi.h>
#include <WebServer.h>
#include <DNSServer.h>
#include <Preferences.h>
#include <ESPmDNS.h>

namespace BotNet {

struct Options {
    String apPrefix       = "BallBot";     // AP: BallBot-AB12
    String apPassword     = "ballbot123";  // 8-63 ky tu
    String mdnsName       = "ballbot";     // ballbot.local (khi da noi router)
    String defaultSsid;
    String defaultPass;
    String defaultPcIp;                    // IP laptop mac dinh (che do Fixed)
    uint16_t defaultPcPort = 5005;
    String defaultWebPass = "admin123";    // mat khau dang nhap trang web
    int bootButtonPin     = 0;             // -1 de tat reset bang nut BOOT
};

// ------------------------------------------------------------------ giao dien
static const char kPage[] = R"rawliteral(<!DOCTYPE html>
<html lang="en"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover">
<meta name="theme-color" content="#0b1220"><title>BallBot</title>
<style>
:root{--bg:#0b1220;--bg2:#0f1a2e;--card:#121d33;--line:#22314f;--t:#e8eef9;--m:#8396b5;--g:#22d37f;--g2:#15a862;--y:#f7b733;--r:#ff5f5f;--c:#38c6f4}
*{box-sizing:border-box;-webkit-tap-highlight-color:transparent}
html,body{margin:0;background:radial-gradient(1200px 600px at 50% -200px,#16305a 0,var(--bg) 60%) fixed;color:var(--t);font:15px/1.45 system-ui,-apple-system,"Segoe UI",Roboto,sans-serif}
.w{max-width:480px;margin:0 auto;padding:18px 14px 48px}
.top{display:flex;align-items:center;gap:12px;margin-bottom:16px}
.logo{width:42px;height:42px;border-radius:12px;background:linear-gradient(135deg,#1e3a66,#0f2140);display:grid;place-items:center;box-shadow:0 4px 18px #0006}
.logo i{width:20px;height:20px;border-radius:50%;background:radial-gradient(circle at 35% 30%,#fff9,#0000 45%),var(--y);box-shadow:0 0 12px #f7b73388}
.top h1{margin:0;font-size:18px;letter-spacing:.4px}.top small{display:block;color:var(--m);font-size:12.5px}
.chip{margin-left:auto;padding:5px 10px;border-radius:30px;font-size:12px;font-weight:700;white-space:nowrap}
.ok{background:#123826;color:var(--g)}.wa{background:#3b3012;color:var(--y)}.no{background:#3d171b;color:var(--r)}
.card{background:linear-gradient(180deg,#14213a,var(--card));border:1px solid var(--line);border-radius:16px;padding:14px;margin-bottom:12px;box-shadow:0 6px 24px #0005}
.hd{display:flex;align-items:center;justify-content:space-between;margin-bottom:10px}
.hd b{font-size:12px;letter-spacing:1.3px;color:var(--m);text-transform:uppercase}
.tiles{display:grid;grid-template-columns:1fr 1fr;gap:8px}
.tile{background:var(--bg2);border:1px solid var(--line);border-radius:12px;padding:10px 11px;min-width:0}
.tile span{display:block;font-size:11px;letter-spacing:1px;color:var(--m);text-transform:uppercase}
.tile b{display:block;font-size:15.5px;margin-top:2px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
.tile.wide{grid-column:1/-1}
label{display:block;font-size:12.5px;color:var(--m);margin:12px 0 5px}
.in{position:relative}
input[type=text],input[type=password],input[type=number]{width:100%;padding:12px 13px;border-radius:11px;border:1px solid var(--line);background:#0a1426;color:var(--t);font-size:16px;outline:none;transition:border .15s}
input:focus{border-color:var(--c)}
.eye{position:absolute;right:6px;top:50%;transform:translateY(-50%);background:none;border:0;color:var(--m);font-size:13px;padding:8px;cursor:pointer}
.btn{display:block;width:100%;margin-top:14px;padding:13px;border:0;border-radius:11px;background:linear-gradient(180deg,#2be08b,var(--g2));color:#03150b;font-weight:800;font-size:15px;letter-spacing:.3px;cursor:pointer}
.btn:active{transform:translateY(1px)}.btn:disabled{opacity:.5}
.btn.s{background:#1d2c48;color:var(--t)}.btn.d{background:#3a1820;color:#ff9a9a}
.sm{width:auto;margin:0;padding:7px 13px;font-size:13px}
.row2{display:flex;gap:8px}.row2 .btn{margin-top:0}
.list{max-height:280px;overflow:auto;border:1px solid var(--line);border-radius:12px;background:#0a1426}
.net{display:flex;align-items:center;gap:11px;padding:12px;border-bottom:1px solid #18243d;cursor:pointer}
.net:last-child{border:0}.net.sel{background:#103425}.net .n{flex:1;min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
.net small{color:var(--m);font-size:12px}.cur{color:var(--g);font-size:11px;font-weight:700;margin-left:6px}
.empty{padding:16px;color:var(--m);font-size:14px;text-align:center}
.bars{display:inline-flex;align-items:flex-end;gap:2px;height:16px;width:20px}.bars i{width:3px;background:#304260;border-radius:1px}.bars i.on{background:var(--g)}
.seg{display:flex;background:#0a1426;border:1px solid var(--line);border-radius:12px;padding:3px}
.seg button{flex:1;padding:10px;border:0;border-radius:9px;background:none;color:var(--m);font-weight:800;font-size:14px}
.seg button.on{background:linear-gradient(180deg,#2be08b,var(--g2));color:#03150b}
.hint{font-size:12.5px;color:var(--m);margin-top:9px}
.ck{display:flex;align-items:center;gap:9px;margin-top:12px;color:var(--t);font-size:14px}
.ck input{width:19px;height:19px;accent-color:var(--g)}
details summary{list-style:none;cursor:pointer;display:flex;justify-content:space-between}
details summary::-webkit-details-marker{display:none}
details summary b{font-size:12px;letter-spacing:1.3px;color:var(--m);text-transform:uppercase}
details summary:after{content:"\25BE";color:var(--m)}details[open] summary:after{content:"\25B4"}
.toast{position:fixed;left:50%;bottom:22px;transform:translate(-50%,30px);opacity:0;transition:.25s;background:#1b2b47;border:1px solid var(--line);padding:11px 16px;border-radius:12px;font-size:14px;max-width:92%;box-shadow:0 8px 30px #000a;z-index:9}
.toast.on{opacity:1;transform:translate(-50%,0)}.toast.e{border-color:#6b2730;color:#ffb0b0}.toast.k{border-color:#1f6b45;color:#9ff0c6}
.hide{display:none!important}
.center{text-align:center;color:var(--m);font-size:13px;margin-top:14px}
</style></head><body><div class="w">

<div class="top"><div class="logo"><i></i></div>
<div><h1>BallBot Setup</h1><small id="apn">-</small></div>
<span id="chip" class="chip wa">...</span></div>

<div id="vApp">
 <div class="card">
  <div class="hd"><b>Robot status</b><span id="upt" class="hint" style="margin:0"></span></div>
  <div class="tiles">
   <div class="tile wide"><span>Router WiFi</span><b id="tWifi">-</b></div>
   <div class="tile"><span>ESP32 IP</span><b id="tIp">-</b></div>
   <div class="tile"><span>Signal</span><b id="tSig">-</b></div>
   <div class="tile"><span>Battery</span><b id="tBat">-</b></div>
   <div class="tile"><span>Commands</span><b id="tCmd">-</b></div>
   <div class="tile wide"><span>Telemetry to laptop</span><b id="tPc">-</b></div>
  </div>
 </div>

 <div class="card">
  <div class="hd"><b>WiFi network</b><button class="btn s sm" id="bScan" type="button">Scan</button></div>
  <div id="list" class="list"><div class="empty">Tap Scan to find networks</div></div>
  <label>Network (SSID)</label>
  <input id="ws" type="text" maxlength="32" autocomplete="off" autocapitalize="none" placeholder="type manually for hidden WiFi">
  <label>Password</label>
  <div class="in"><input id="wp" type="password" maxlength="63" autocomplete="new-password" placeholder="empty = keep saved password"><button type="button" class="eye" data-for="wp">SHOW</button></div>
  <button class="btn" id="bWifi" type="button">Save &amp; connect</button>
  <div class="hint">2.4 GHz only. Your phone may drop from the hotspot for a few seconds while the robot connects &mdash; just reconnect.</div>
 </div>

 <div class="card">
  <div class="hd"><b>Laptop link</b></div>
  <div class="seg"><button id="mA" type="button">Auto</button><button id="mF" type="button">Fixed IP</button></div>
  <div id="hA" class="hint">Robot broadcasts telemetry until the laptop (Brain) replies, then locks onto that laptop. No IP to type, survives IP changes. Some school/office WiFi blocks broadcast &mdash; use Fixed IP there.</div>
  <div id="hF">
   <label>Laptop IP</label>
   <input id="pi" type="text" inputmode="decimal" placeholder="e.g. 192.168.1.20">
   <button class="btn s hide" id="bSeen" type="button"></button>
  </div>
  <label>Telemetry port</label>
  <input id="pp" type="number" min="1" max="65535">
  <button class="btn" id="bPc" type="button">Save laptop link</button>
 </div>

 <div class="card"><details><summary><b>Hotspot password</b></summary>
  <label>Current hotspot password</label><input id="so" type="password" autocomplete="current-password">
  <label>New hotspot password (8+ chars)</label><input id="sa" type="password" autocomplete="new-password">
  <button class="btn" id="bSec" type="button">Change hotspot password</button>
  <div class="hint">Forgot the password? Hold BOOT on the ESP32 for 5 s to restore defaults.</div>
 </details></div>

 <button class="btn s" id="bReb" type="button">Restart</button>
 <div class="center" id="mdn"></div>
</div>
</div><div id="toast" class="toast"></div>
<script>
var $=function(i){return document.getElementById(i)},S={},auto=false,dirtyPc=false,poll=null,tries=0,tm=null;
function ls(){try{return window.localStorage}catch(e){return null}}
var T='';try{T=(ls()&&ls().getItem('bbt'))||sessionStorage.getItem('bbt')||''}catch(e){}
function setT(t,rem){T=t||'';try{sessionStorage.setItem('bbt',T);if(ls()){if(rem&&T)ls().setItem('bbt',T);else if(!T)ls().removeItem('bbt')}}catch(e){}}
function q(u){return u+(u.indexOf('?')<0?'?':'&')+'t='+encodeURIComponent(T)}
function toast(t,ok){var e=$('toast');e.textContent=t;e.className='toast on '+(ok?'k':'e');clearTimeout(e._t);e._t=setTimeout(function(){e.className='toast'},2800)}
function esc(s){return String(s).replace(/[&<>"]/g,function(c){return{'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;'}[c]})}
function bars(r){var n=r>-55?4:r>-66?3:r>-77?2:1,h='';for(var i=1;i<=4;i++)h+='<i class="'+(i<=n?'on':'')+'" style="height:'+(i*3+4)+'px"></i>';return '<span class="bars">'+h+'</span>'}
function post(u,d){d.t=T;var qs=Object.keys(d).map(function(k){return encodeURIComponent(k)+'='+encodeURIComponent(d[k])}).join('&');
  return fetch(u+'?'+qs,{cache:'no-store'}).then(function(r){return r.text().then(function(x){
    try{return JSON.parse(x)}catch(e){
      var tk=(x.match(/"token"\s*:\s*"([0-9a-f]{32})"/)||[])[1];
      if(/"ok"\s*:\s*"?true/.test(x))return{ok:true,token:tk,ap:/"ap"\s*:\s*true/.test(x)};
      return{ok:false,err:'Bad reply ('+r.status+'): '+(x.slice(0,60)||'empty')}}})})
  .then(function(j){if(!j.ok)throw new Error(j.err||'Error');return j})}

function setMode(a){auto=a;$('mA').className=a?'on':'';$('mF').className=a?'':'on';$('hA').classList.toggle('hide',!a);$('hF').classList.toggle('hide',a)}
function fmtUp(s){var h=Math.floor(s/3600),m=Math.floor(s%3600/60);return 'up '+(h?h+'h ':'')+m+'m'}
function refresh(){fetch(q('/api/state'),{cache:'no-store'}).then(function(r){return r.json()}).then(function(s){
  S=s;$('apn').textContent='Hotspot '+s.ap;
  var c=$('chip');
  if(s.connected){c.className='chip ok';c.textContent='Online'}else if(s.paused){c.className='chip wa';c.textContent='Paused'}else{c.className='chip no';c.textContent='Offline'}
  $('tWifi').innerHTML=s.connected?esc(s.ssid):(esc(s.ssid||'not set')+' &middot; <span style="color:var(--y)">'+(s.why?s.why:(s.paused?'paused while phone connected':'connecting...'))+'</span>');
  $('tIp').textContent=s.connected?s.ip:'-';
  $('tSig').innerHTML=s.connected?bars(s.rssi)+' '+s.rssi+' dBm':'-';
  $('tBat').textContent=s.bat.toFixed(2)+' V';
  $('tCmd').innerHTML=s.cmd_rate>0?'<span style="color:var(--g)">'+s.cmd_rate.toFixed(0)+' /s</span>':'<span style="color:var(--m)">none</span>';
  $('tPc').innerHTML=esc(s.pc_target)+':'+s.pc_port+(s.pc_auto?(s.pc_seen?' <span style="color:var(--g)">&middot; locked</span>':' <span style="color:var(--y)">&middot; searching</span>'):'')+' &middot; '+s.tele_rate.toFixed(0)+'/s';
  $('upt').textContent=fmtUp(s.uptime);
  $('mdn').textContent=s.connected?('Also on your network: http://'+s.ip+'  ('+s.mdns+'.local) — view only'):'';
  if(!dirtyPc){setMode(s.pc_auto);$('pi').value=s.pc_ip;$('pp').value=s.pc_port}
  if(!$('ws').value&&document.activeElement!==$('ws'))$('ws').value=s.ssid;
  var sn=s.pc_seen&&s.pc_seen!==$('pi').value;$('bSeen').classList.toggle('hide',!sn);if(sn)$('bSeen').textContent='Use laptop sending commands: '+s.pc_seen;
}).catch(function(){var c=$('chip');c.className='chip no';c.textContent='No link'})}
function render(l){var b=$('list');b.innerHTML='';if(!l.length){b.innerHTML='<div class="empty">No networks found</div>';return}
  l.forEach(function(w){var d=document.createElement('div');d.className='net'+(w.s===$('ws').value?' sel':'');
   d.innerHTML=bars(w.r)+'<span class="n"></span><small></small>';
   d.querySelector('.n').innerHTML=esc(w.s)+(w.s===S.ssid?'<span class="cur">SAVED</span>':'');
   d.querySelector('small').textContent=(w.o?'open':'🔒')+' '+w.r;
   d.onclick=function(){$('ws').value=w.s;$('wp').value='';[].forEach.call(b.children,function(x){x.classList.remove('sel')});d.classList.add('sel');$('wp').placeholder=w.o?'open network, no password':(w.s===S.ssid?'empty = keep saved password':'WiFi password');if(!w.o)$('wp').focus()};
   b.appendChild(d)})}
function scan(force){var b=$('bScan');b.disabled=true;b.textContent='Scanning';if(force){tries=0;$('list').innerHTML='<div class="empty">Scanning...</div>'}
  fetch(q('/api/scan'+(force?'?start=1':'')),{cache:'no-store'}).then(function(r){return r.json()}).then(function(d){
   if(d.state==='scanning'){if(++tries>20){b.disabled=false;b.textContent='Scan';return}clearTimeout(tm);tm=setTimeout(function(){scan(false)},1000);return}
   b.disabled=false;b.textContent='Rescan';tries=0;
   if(d.state==='failed'){$('list').innerHTML='<div class="empty">Scan failed, tap Rescan</div>';return}render(d.list||[])
  }).catch(function(){if(++tries>20){b.disabled=false;b.textContent='Scan';return}clearTimeout(tm);tm=setTimeout(function(){scan(false)},1500)})}
[].forEach.call(document.querySelectorAll('.eye'),function(e){e.onclick=function(){var i=$(e.getAttribute('data-for'));i.type=i.type==='password'?'text':'password';e.textContent=i.type==='password'?'SHOW':'HIDE'}});
$('bScan').onclick=function(){scan(true)};
$('bWifi').onclick=function(){var s=$('ws').value.trim();if(!s){toast('Choose a network');return}
  post('/api/wifi',{ssid:s,pass:$('wp').value}).then(function(){$('wp').value='';toast('Saved. Connecting to '+s+'...',1)}).catch(function(e){toast(e.message)})};
$('mA').onclick=function(){dirtyPc=true;setMode(true)};$('mF').onclick=function(){dirtyPc=true;setMode(false)};
$('pi').oninput=$('pp').oninput=function(){dirtyPc=true};
$('bSeen').onclick=function(){dirtyPc=true;setMode(false);$('pi').value=S.pc_seen};
$('bPc').onclick=function(){post('/api/pc',{auto:auto?1:0,ip:$('pi').value.trim(),port:$('pp').value}).then(function(){dirtyPc=false;toast('Laptop link saved',1);refresh()}).catch(function(e){toast(e.message)})};
$('bSec').onclick=function(){post('/api/security',{old:$('so').value,appass:$('sa').value}).then(function(){['so','sa'].forEach(function(i){$(i).value=''});toast('Saved. Hotspot restarts - reconnect with the new password',1)}).catch(function(e){toast(e.message)})};
$('bReb').onclick=function(){if(confirm('Restart the ESP32? Robot stops for a few seconds.'))post('/api/reboot',{}).then(function(){toast('Restarting...',1)}).catch(function(e){toast(e.message)})};
refresh();setInterval(refresh,2000);setTimeout(function(){scan(false)},400);
</script></body></html>)rawliteral";

// ------------------------------------------------------------------ manager
class Manager;
inline Manager& manager();

class Manager {
public:
    void begin(const Options& o) {
        opt_ = o;
        loadConfig();
        buildApName();
        if (opt_.bootButtonPin >= 0) pinMode(opt_.bootButtonPin, INPUT_PULLUP);

        WiFi.persistent(false);              // khong ghi flash WiFi moi lan begin()
        WiFi.setAutoReconnect(false);        // tu quan ly ket noi lai -> AP khong nhay kenh lien tuc
        WiFi.setHostname(opt_.mdnsName.c_str());
        WiFi.mode(WIFI_AP_STA);
        WiFi.onEvent([](WiFiEvent_t e, WiFiEventInfo_t info) {   // ghi lai ly do mat / khong ket noi router
            if (e == ARDUINO_EVENT_WIFI_STA_DISCONNECTED) manager().lastReason_ = info.wifi_sta_disconnected.reason;
            if (e == ARDUINO_EVENT_WIFI_STA_GOT_IP) manager().lastReason_ = 0;
        });

        startAp();

        dns_ = new DNSServer();
        dns_->start(53, "*", WiFi.softAPIP());   // captive portal
        server_ = new WebServer(80);
        setupRoutes();
        server_->begin();

        connectSta();
    }

    void loop() {
        if (!server_ || !dns_) return;
        dns_->processNextRequest();
        server_->handleClient();

        uint32_t now = millis();
        bool conn = connected();
        if (conn && !wasConnected_) onConnected();
        else if (!conn && wasConnected_) {
            Serial.println("[BotNet] WiFi lost");
            lastTryMs_ = now - RETRY_INTERVAL_MS + 3000;   // thu lai sau ~3 s
        }
        wasConnected_ = conn;

        if (pendingApply_ && (int32_t)(now - applyAt_) >= 0) {
            pendingApply_ = false;
            forceTryUntil_ = now + 30000;          // vua bam Save: thu ket noi 30 s du dien thoai dang noi
            connectSta();
        }
        if (!conn && (int32_t)(forceTryUntil_ - now) > 0 && (now - lastTryMs_) > 10000) connectSta();
        if (pendingAp_ && (int32_t)(now - apAt_) >= 0) { pendingAp_ = false; startAp(); }
        if (pendingReboot_ && (int32_t)(now - rebootAt_) >= 0) { delay(50); ESP.restart(); }

        // Ket noi lai dinh ky - KHONG thu khi dien thoai dang noi vao AP
        if (!conn && ssid_.length() > 0 && WiFi.softAPgetStationNum() == 0 && !scanRunning_ &&
            (now - lastTryMs_) > RETRY_INTERVAL_MS) {
            connectSta();
        }

        if (pcLearned_ && (now - lastPcMs_) > PC_LOST_MS) pcLearned_ = false;

        if (now - rateAt_ >= 1000) {
            float k = 1000.0f / (float)(now - rateAt_);
            cmdRate_ = cmdCount_ * k;
            teleRate_ = teleCount_ * k;
            cmdCount_ = teleCount_ = 0;
            rateAt_ = now;
        }

        if (opt_.bootButtonPin >= 0) {
            if (digitalRead(opt_.bootButtonPin) == LOW) {
                if (btnDownMs_ == 0) btnDownMs_ = now ? now : 1;
                else if (now - btnDownMs_ > 5000) {
                    Serial.println("[BotNet] BOOT held 5 s -> factory reset");
                    resetConfig();
                    delay(100);
                    ESP.restart();
                }
            } else btnDownMs_ = 0;
        }
    }

    bool connected() const { return WiFi.status() == WL_CONNECTED; }
    const String& apName() const { return apName_; }

    // Dich gui telemetry
    IPAddress pcTarget() const {
        if (!pcAuto_) return pcIp_;
        if (pcLearned_) return learnedPc_;
        if (WiFi.status() == WL_CONNECTED) return WiFi.broadcastIP();
        return IPAddress(192, 168, 4, 255);
    }
    uint16_t pcPort() const { return pcPort_; }

    // Goi khi nhan duoc lenh "cmd:" tu laptop
    void notePcCommand(const IPAddress& from) {
        learnedPc_ = from;
        pcLearned_ = true;
        lastPcMs_ = millis();
        cmdCount_++;
    }
    // Goi moi lan gui telemetry
    void noteTelemetry(float batteryV) {
        teleCount_++;
        bat_ = batteryV;
    }

private:
    static const uint32_t RETRY_INTERVAL_MS = 20000UL;
    static const uint32_t SCAN_CACHE_MS = 60000UL;
    static const uint32_t PC_LOST_MS = 5000UL;

    Options opt_;
    Preferences prefs_;
    WebServer* server_ = nullptr;   // tao trong begin() sau khi WiFi da chay
    DNSServer* dns_ = nullptr;

    String apName_, apPass_;
    String ssid_, pass_, webPass_, token_;
    bool pcAuto_ = false;
    IPAddress pcIp_;
    uint16_t pcPort_ = 5005;

    IPAddress learnedPc_;
    bool pcLearned_ = false;
    uint32_t lastPcMs_ = 0;
    uint32_t cmdCount_ = 0, teleCount_ = 0, rateAt_ = 0;
    float cmdRate_ = 0, teleRate_ = 0, bat_ = 0;

    bool wasConnected_ = false;
    bool pendingApply_ = false, pendingAp_ = false, pendingReboot_ = false;
    uint32_t applyAt_ = 0, apAt_ = 0, rebootAt_ = 0, lastTryMs_ = 0, btnDownMs_ = 0, forceTryUntil_ = 0;
public:
    volatile int lastReason_ = 0;      // ly do ngat ket noi STA (wifi_err_reason_t)
private:

    bool scanRunning_ = false;
    String scanJson_;
    uint32_t scanAtMs_ = 0;

    // ---------------------------------------------------------------- config
    static String randomToken() {
        char t[33];
        for (int i = 0; i < 4; i++) snprintf(t + i * 8, 9, "%08lx", (unsigned long)esp_random());
        return String(t);
    }

    void loadConfig() {
        prefs_.begin("ballbot", false);
        ssid_    = prefs_.getString("ssid", opt_.defaultSsid);
        pass_    = prefs_.getString("pass", opt_.defaultPass);
        pcAuto_  = prefs_.getBool("pcauto", false);
        String ip = prefs_.getString("pcip", opt_.defaultPcIp);
        if (!pcIp_.fromString(ip)) pcIp_.fromString(opt_.defaultPcIp);
        pcPort_  = prefs_.getUShort("pcport", opt_.defaultPcPort);
        webPass_ = prefs_.getString("webpass", opt_.defaultWebPass);
        apPass_  = prefs_.getString("appass", opt_.apPassword);
        token_   = prefs_.getString("token", "");
        if (token_.length() != 32) { token_ = randomToken(); prefs_.putString("token", token_); }
    }

    void resetConfig() {
        prefs_.clear();
        ssid_ = opt_.defaultSsid;
        pass_ = opt_.defaultPass;
        pcAuto_ = false;
        pcIp_.fromString(opt_.defaultPcIp);
        pcPort_ = opt_.defaultPcPort;
        webPass_ = opt_.defaultWebPass;
        apPass_ = opt_.apPassword;
        token_ = randomToken();
        prefs_.putString("token", token_);
    }

    void buildApName() {
        uint64_t mac = ESP.getEfuseMac();
        char suffix[8];
        snprintf(suffix, sizeof(suffix), "%02X%02X", (unsigned)((mac >> 32) & 0xFF), (unsigned)((mac >> 40) & 0xFF));
        apName_ = opt_.apPrefix + "-" + String(suffix);
    }

    // ---------------------------------------------------------------- WiFi
    void startAp() {
        String pass = apPass_;
        if (pass.length() > 0 && (pass.length() < 8 || pass.length() > 63)) pass = "ballbot123";
        bool ok = WiFi.softAP(apName_.c_str(), pass.length() ? pass.c_str() : nullptr);
        Serial.printf("[BotNet] Hotspot '%s' %s | password: %s | open http://%s\n", apName_.c_str(),
                      ok ? "OK" : "FAILED", pass.c_str(), WiFi.softAPIP().toString().c_str());
    }

    void connectSta() {
        lastTryMs_ = millis();
        if (ssid_.length() == 0) return;
        Serial.printf("[BotNet] Connecting to '%s' ...\n", ssid_.c_str());
        WiFi.disconnect(false, false);           // giu nguyen AP
        delay(100);                               // core 2.x: begin() ngay sau disconnect() co the bi bo qua
        WiFi.begin(ssid_.c_str(), pass_.length() ? pass_.c_str() : nullptr);
    }

    static const char* reasonText(int r) {
        switch (r) {
            case 0: return "";
            case 201: return "network not found (2.4 GHz? in range?)";
            case 2: case 15: case 202: case 204: return "wrong password";
            case 203: return "router refused connection";
            case 200: return "signal lost";
            case 8: return "disconnected";
            default: { static char b[24]; snprintf(b, sizeof(b), "error %d", r); return b; }
        }
    }

    void onConnected() {
        Serial.printf("[BotNet] Connected '%s'  ESP32 IP %s\n", WiFi.SSID().c_str(), WiFi.localIP().toString().c_str());
        MDNS.end();
        if (MDNS.begin(opt_.mdnsName.c_str())) MDNS.addService("http", "tcp", 80);
    }

    // ---------------------------------------------------------------- helpers
    static String jsonEsc(const String& s) {
        String o;
        o.reserve(s.length() + 4);
        for (size_t i = 0; i < s.length(); i++) {
            char c = s[i];
            if (c == '"' || c == '\\') { o += '\\'; o += c; }
            else if ((uint8_t)c < 0x20) o += ' ';
            else o += c;
        }
        return o;
    }

    bool fromAp() { return server_->client().localIP() == WiFi.softAPIP(); }

    // Khong co dang nhap web: mat khau hotspot la lop bao ve (chi sua duoc qua hotspot).
    bool authed() { return true; }

    void sendJson(int code, const String& j) {
        server_->setContentLength(CONTENT_LENGTH_NOT_SET);   // bo che do "chunked" con sot tu trang chu
        server_->sendHeader("Cache-Control", "no-store");
        server_->send(code, "application/json", j);
    }
    void ok(const String& extra = String()) {
        String j;
        j.reserve(extra.length() + 16);
        j += "{\"ok\":true";
        j += extra;
        j += "}";
        sendJson(200, j);
    }
    void err(const String& m) {
        String j;
        j.reserve(m.length() + 32);
        j += "{\"ok\":false,\"err\":\"";
        j += jsonEsc(m);
        j += "\"}";
        sendJson(200, j);
    }

    // Sua cau hinh: phai noi qua hotspot cua ESP32 VA da dang nhap
    bool denyWrite() {
        if (!fromAp()) { err("Settings can only be changed via the robot hotspot " + apName_); return true; }
        if (!authed()) { sendJson(200, "{\"ok\":false,\"auth\":false,\"err\":\"Please sign in\"}"); return true; }
        return false;
    }

    // ---------------------------------------------------------------- routes
    void setupRoutes() {
        server_->on("/", HTTP_GET, [this]() { handleRoot(); });
        server_->on("/api/state", HTTP_GET, [this]() { handleState(); });
        server_->on("/api/scan", HTTP_GET, [this]() { handleScan(); });
        server_->on("/api/wifi", HTTP_ANY, [this]() { handleWifi(); });
        server_->on("/api/pc", HTTP_ANY, [this]() { handlePc(); });
        server_->on("/api/security", HTTP_ANY, [this]() { handleSecurity(); });
        server_->on("/api/reboot", HTTP_ANY, [this]() { handleReboot(); });
        server_->onNotFound([this]() { handleNotFound(); });
    }

    void handleNotFound() {
        if (!fromAp()) { server_->send(404, "text/plain", "Not found"); return; }
        // captive portal: generate_204, hotspot-detect.html, connecttest.txt ... -> trang cau hinh
        server_->sendHeader("Location", String("http://") + WiFi.softAPIP().toString() + "/", true);
        server_->send(302, "text/plain", "");
    }

    void handleRoot() {
        server_->sendHeader("Cache-Control", "no-store");
        server_->setContentLength(CONTENT_LENGTH_UNKNOWN);
        server_->send(200, "text/html", "");
        const size_t total = strlen(kPage), step = 1400;   // gui tung phan, it ton RAM
        for (size_t i = 0; i < total; i += step)
            server_->sendContent(kPage + i, (total - i) < step ? (total - i) : step);
        server_->sendContent("");
    }

    void handleState() {
        bool conn = connected();
        bool au = authed() || !fromAp();      // tu LAN: chi xem trang thai (khong sua duoc)
        String j = "{\"ap\":\"" + jsonEsc(apName_) + "\"";
        j += String(",\"auth\":") + (au ? "true" : "false");
        if (au) {
            j += String(",\"connected\":") + (conn ? "true" : "false");
            j += String(",\"paused\":") + (!conn && WiFi.softAPgetStationNum() > 0 ? "true" : "false");
            j += ",\"ssid\":\"" + jsonEsc(conn ? WiFi.SSID() : ssid_) + "\"";
            j += ",\"ip\":\"" + (conn ? WiFi.localIP().toString() : String("")) + "\"";
            j += ",\"rssi\":" + String(conn ? WiFi.RSSI() : 0);
            j += ",\"mdns\":\"" + jsonEsc(opt_.mdnsName) + "\"";
            j += String(",\"pc_auto\":") + (pcAuto_ ? "true" : "false");
            j += ",\"pc_ip\":\"" + pcIp_.toString() + "\"";
            j += ",\"pc_port\":" + String(pcPort_);
            j += ",\"pc_target\":\"" + pcTarget().toString() + "\"";
            j += ",\"pc_seen\":\"" + (pcLearned_ ? learnedPc_.toString() : String("")) + "\"";
            j += ",\"cmd_rate\":" + String(cmdRate_, 1);
            j += ",\"tele_rate\":" + String(teleRate_, 1);
            j += ",\"bat\":" + String(bat_, 2);
            j += ",\"uptime\":" + String(millis() / 1000);
            j += ",\"why\":\"" + String(reasonText(lastReason_)) + "\"";
        }
        j += "}";
        sendJson(200, j);
    }


    // Danh sach WiFi: bo trung ten (giu song manh nhat), sap theo song, toi da 20
    void buildScanJson(int n) {
        static const int MAXN = 20;
        String names[MAXN];
        int rssi[MAXN];
        bool open[MAXN];
        int cnt = 0;
        for (int i = 0; i < n; i++) {
            String s = WiFi.SSID(i);
            if (s.length() == 0) continue;
            int r = WiFi.RSSI(i);
            bool o = WiFi.encryptionType(i) == WIFI_AUTH_OPEN;
            int dup = -1;
            for (int k = 0; k < cnt; k++) if (names[k] == s) { dup = k; break; }
            if (dup >= 0) { if (r > rssi[dup]) { rssi[dup] = r; open[dup] = o; } continue; }
            if (cnt < MAXN) { names[cnt] = s; rssi[cnt] = r; open[cnt] = o; cnt++; }
        }
        for (int a = 1; a < cnt; a++) {
            String ns = names[a]; int nr = rssi[a]; bool no = open[a];
            int b = a - 1;
            while (b >= 0 && rssi[b] < nr) { names[b + 1] = names[b]; rssi[b + 1] = rssi[b]; open[b + 1] = open[b]; b--; }
            names[b + 1] = ns; rssi[b + 1] = nr; open[b + 1] = no;
        }
        String j = "[";
        for (int k = 0; k < cnt; k++) {
            if (k) j += ",";
            j += "{\"s\":\"" + jsonEsc(names[k]) + "\",\"r\":" + String(rssi[k]) + ",\"o\":" + String(open[k] ? 1 : 0) + "}";
        }
        j += "]";
        scanJson_ = j;
        scanAtMs_ = millis();
    }

    void sendScan(const char* state, bool withList) {
        String j = String("{\"state\":\"") + state + "\"";
        if (withList) j += ",\"list\":" + scanJson_;
        j += "}";
        sendJson(200, j);
    }

    void handleScan() {
        if (!fromAp() || !authed()) { sendScan("failed", false); return; }
        bool force = server_->hasArg("start");
        if (scanRunning_) {
            int n = WiFi.scanComplete();
            if (n == WIFI_SCAN_RUNNING) { sendScan("scanning", false); return; }
            scanRunning_ = false;
            if (n < 0) { sendScan("failed", false); return; }
            buildScanJson(n);
            WiFi.scanDelete();
        }
        if (!force && scanJson_.length() > 0 && (millis() - scanAtMs_) < SCAN_CACHE_MS) { sendScan("done", true); return; }
        int r = WiFi.scanNetworks(true, false);     // bat dong bo: khong chan motor
        if (r == WIFI_SCAN_FAILED) { sendScan("failed", false); return; }
        scanRunning_ = true;
        sendScan("scanning", false);
    }

    void handleWifi() {
        if (denyWrite()) return;
        String s = server_->arg("ssid"), p = server_->arg("pass");
        s.trim();
        if (s.length() == 0 || s.length() > 32) { err("Invalid network name"); return; }
        if (p.length() == 0 && s == ssid_) p = pass_;               // de trong = giu mat khau cu
        if (p.length() > 0 && (p.length() < 8 || p.length() > 63)) { err("WiFi password must be 8-63 characters"); return; }
        ssid_ = s; pass_ = p;
        prefs_.putString("ssid", ssid_);
        prefs_.putString("pass", pass_);
        pcLearned_ = false;
        pendingApply_ = true;
        applyAt_ = millis() + 800;                  // tra loi dien thoai xong moi doi WiFi
        Serial.printf("[BotNet] New WiFi saved: '%s'\n", ssid_.c_str());
        ok();
    }

    void handlePc() {
        if (denyWrite()) return;
        bool a = server_->arg("auto") == "1";
        IPAddress ip;
        if (!a && !ip.fromString(server_->arg("ip"))) { err("Invalid IP address"); return; }
        long port = server_->arg("port").toInt();
        if (port < 1 || port > 65535) { err("Invalid port"); return; }
        pcAuto_ = a;
        if (!a) pcIp_ = ip;
        pcPort_ = (uint16_t)port;
        prefs_.putBool("pcauto", pcAuto_);
        prefs_.putString("pcip", pcIp_.toString());
        prefs_.putUShort("pcport", pcPort_);
        pcLearned_ = false;
        ok();
    }

    void handleSecurity() {
        if (denyWrite()) return;
        if (server_->arg("old") != apPass_) { err("Current hotspot password is wrong"); return; }
        String ap = server_->arg("appass");
        if (ap.length() < 8 || ap.length() > 63) { err("Hotspot password: 8-63 characters"); return; }
        apPass_ = ap;
        prefs_.putString("appass", apPass_);
        pendingAp_ = true;
        apAt_ = millis() + 800;
        ok();
    }

    void handleReboot() {
        if (denyWrite()) return;
        pendingReboot_ = true;
        rebootAt_ = millis() + 600;
        ok();
    }

    void handleReset() {
        if (denyWrite()) return;
        resetConfig();
        pendingReboot_ = true;
        rebootAt_ = millis() + 600;
        ok();
    }
};

inline Manager& manager() {
    static Manager m;
    return m;
}

}  // namespace BotNet
