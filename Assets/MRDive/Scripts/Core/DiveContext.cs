using UnityEngine;

namespace Livisor.MRDive
{
    /// <summary>
    /// 演出 1 回ぶんの舞台道具。Director が組み立てて各 Transition に渡す。
    /// Transition 側はここに入っているもの以外に触らなくて済むようにしている。
    /// </summary>
    public sealed class DiveContext
    {
        /// <summary>この演出を回している Director。</summary>
        public DiveDirector Director { get; }

        /// <summary>頭（centerEyeAnchor 相当）。演出の基準点はすべてここ。</summary>
        public Transform Head { get; }

        /// <summary>視点カメラ。FOV や near clip を見たいときに。</summary>
        public Camera EyeCamera { get; }

        /// <summary>現実の見え方を操作する窓口。</summary>
        public PassthroughBridge Passthrough { get; }

        /// <summary>視界を覆うレイヤーの置き場。</summary>
        public DiveOverlay Overlay { get; }

        /// <summary>効果音を鳴らしたいとき用。未設定なら null。</summary>
        public AudioSource Audio { get; }

        public DiveContext(
            DiveDirector director,
            Transform head,
            Camera eyeCamera,
            PassthroughBridge passthrough,
            DiveOverlay overlay,
            AudioSource audio)
        {
            Director = director;
            Head = head;
            EyeCamera = eyeCamera;
            Passthrough = passthrough;
            Overlay = overlay;
            Audio = audio;
        }

        /// <summary>頭の正面 distance[m] のワールド座標。</summary>
        public Vector3 ForwardPoint(float distance)
        {
            if (Head == null) return Vector3.forward * distance;
            return Head.position + Head.forward * distance;
        }

        /// <summary>ワンショットで音を鳴らす。AudioSource が無ければ黙って無視。</summary>
        public void PlayOneShot(AudioClip clip, float volume = 1f)
        {
            if (Audio == null || clip == null) return;
            Audio.PlayOneShot(clip, volume);
        }
    }
}
