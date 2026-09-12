using System;
using UnityEngine;

namespace Livisor.Device
{
    /// <summary>
    /// Android(Meta Quest) でマルチキャスト(mDNS)パケットを送受信するために
    /// 必要な WifiManager.MulticastLock を確保する。using で囲んで使う。
    ///
    ///   using (AndroidMulticastLock.Acquire()) { ...mDNS... }
    ///
    /// Android 以外(Editor 等)では何もしない no-op を返す。
    ///
    /// AndroidManifest に以下の権限が必要:
    ///   android.permission.INTERNET
    ///   android.permission.CHANGE_WIFI_MULTICAST_STATE
    /// </summary>
    public sealed class AndroidMulticastLock : IDisposable
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        AndroidJavaObject _lock;
#endif

        public static AndroidMulticastLock Acquire()
        {
            var self = new AndroidMulticastLock();
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    // getApplicationContext().getSystemService(Context.WIFI_SERVICE)
                    var ctx = activity.Call<AndroidJavaObject>("getApplicationContext");
                    var wifi = ctx.Call<AndroidJavaObject>("getSystemService", "wifi");
                    self._lock = wifi.Call<AndroidJavaObject>("createMulticastLock", "livisor-mdns");
                    self._lock.Call("setReferenceCounted", true);
                    self._lock.Call("acquire");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[mDNS] MulticastLock acquire failed: {e.Message}");
            }
#endif
            return self;
        }

        public void Dispose()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                if (_lock != null)
                {
                    if (_lock.Call<bool>("isHeld")) _lock.Call("release");
                    _lock.Dispose();
                    _lock = null;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[mDNS] MulticastLock release failed: {e.Message}");
            }
#endif
        }
    }
}
