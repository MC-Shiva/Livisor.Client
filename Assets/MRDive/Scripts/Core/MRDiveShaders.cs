using UnityEngine;

namespace Livisor.MRDive
{
    /// <summary>
    /// 演出シェーダーの読み込み口。
    ///
    /// Resources に置いているのは、シーンやマテリアルから参照されていないシェーダーが
    /// ビルドから落ちるのを確実に防ぐため。Built-in RP では Always Included Shaders に
    /// 手で登録するか Resources に置くかの二択で、後者のほうが取り回しがよい。
    /// </summary>
    public static class MRDiveShaders
    {
        const string Root = "MRDive/Shaders/";

        public const string Fade = Root + "MRDive_Fade";
        public const string PortalRift = Root + "MRDive_PortalRift";
        public const string PortalSurface = Root + "MRDive_PortalSurface";
        public const string DigitalDissolve = Root + "MRDive_DigitalDissolve";
        public const string DataStream = Root + "MRDive_DataStream";
        public const string LiquidDive = Root + "MRDive_LiquidDive";
        public const string Ripple = Root + "MRDive_Ripple";

        /// <summary>Resources からシェーダーを読む。見つからなければ null を返し、理由をログに出す。</summary>
        public static Shader Load(string resourcePath)
        {
            var shader = Resources.Load<Shader>(resourcePath);
            if (shader == null)
            {
                Debug.LogError(
                    $"[MRDive] シェーダーが見つかりません: Resources/{resourcePath}.shader\n" +
                    "Assets/MRDive/Resources/ 以下に配置されているか確認してください。");
            }
            return shader;
        }
    }
}
