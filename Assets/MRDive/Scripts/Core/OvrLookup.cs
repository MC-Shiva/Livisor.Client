using System;
using System.Reflection;
using UnityEngine;

namespace Livisor.MRDive
{
    /// <summary>
    /// Meta XR SDK の型を名前で引くための小さなヘルパ。
    /// SDK をアセンブリ参照せずに扱うため、Runtime と Editor の両方から使う。
    /// </summary>
    public static class OvrLookup
    {
        const BindingFlags InstanceFlags =
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy;

        /// <summary>読み込まれている全アセンブリから型を名前で探す。見つからなければ null。</summary>
        public static Type FindType(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return null;

            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type t;
                try { t = assemblies[i].GetType(typeName, false); }
                catch (Exception) { continue; }
                if (t != null) return t;
            }
            return null;
        }

        /// <summary>指定した型のコンポーネントをシーンから 1 つ探す。</summary>
        public static Component FindComponent(string typeName)
        {
            Type type = FindType(typeName);
            if (type == null) return null;

#if UNITY_2023_1_OR_NEWER
            return UnityEngine.Object.FindFirstObjectByType(type, FindObjectsInactive.Include) as Component;
#else
            return UnityEngine.Object.FindObjectOfType(type, true) as Component;
#endif
        }

        /// <summary>
        /// 視点の基準にすべき Transform（OVRCameraRig の CenterEyeAnchor）を返す。
        ///
        /// Camera.main を直接使ってはいけない。OVRCameraRig のプレハブは
        /// LeftEyeAnchor にも MainCamera タグを付けており、Camera.main がそちらを
        /// 返すことがある。そうなると演出の基準点が左目にずれ、パススルーの設定も
        /// CenterEyeAnchor ではなく左目のカメラに当たってしまう。
        /// </summary>
        public static Transform FindCenterEyeAnchor()
        {
            Component rig = FindComponent("OVRCameraRig");
            if (rig != null)
            {
                Type rigType = rig.GetType();

                // v203 では public Transform centerEyeAnchor { get; private set; }。
                // 版によってフィールド実装のこともあるので両方見る。
                var property = rigType.GetProperty("centerEyeAnchor", InstanceFlags);
                if (property != null)
                {
                    try
                    {
                        if (property.GetValue(rig) is Transform fromProperty && fromProperty != null)
                            return fromProperty;
                    }
                    catch (Exception) { /* 下のフォールバックへ */ }
                }

                var field = rigType.GetField("centerEyeAnchor", InstanceFlags);
                if (field != null)
                {
                    try
                    {
                        if (field.GetValue(rig) is Transform fromField && fromField != null)
                            return fromField;
                    }
                    catch (Exception) { /* 下のフォールバックへ */ }
                }

                // プロパティが取れなくても、リグ配下の名前で拾えることが多い。
                Transform byName = FindChildByName(rig.transform, "CenterEyeAnchor");
                if (byName != null) return byName;
            }

            // OVRCameraRig がない構成（素の Camera など）では、
            // MainCamera タグのうち CenterEyeAnchor という名前のものを優先する。
            var cameras = Camera.allCameras;
            for (int i = 0; i < cameras.Length; i++)
            {
                if (cameras[i] != null && cameras[i].name == "CenterEyeAnchor")
                    return cameras[i].transform;
            }

            return null;
        }

        static Transform FindChildByName(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;

            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindChildByName(root.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
