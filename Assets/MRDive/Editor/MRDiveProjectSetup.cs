using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Livisor.MRDive.EditorTools
{
    /// <summary>
    /// 実機（Quest）でパススルーを出すために必要なプロジェクト設定を点検し、必要なら直す。
    ///
    /// シーンをどれだけ正しく組んでも、ここが未設定だと AndroidManifest に
    /// com.oculus.feature.PASSTHROUGH が入らず、実機では現実が一切見えない。
    /// シーン側の点検（Livisor > MR Dive > シーンを検証）とは対象が別なので、メニューを分けてある。
    ///
    /// Meta SDK の型はすべてリフレクション越しに触る。SDK 未導入でもこのファイルが
    /// コンパイルを止めないようにするため（ランタイム側の PassthroughBridge と同じ方針）。
    /// </summary>
    public static class MRDiveProjectSetup
    {
        const string ProjectConfigType = "OVRProjectConfig";
        const string MetaXRFeatureId = "com.meta.openxr.feature.metaxr";

        [MenuItem("Livisor/MR Dive/実機設定を点検", false, 40)]
        public static void Inspect() => Run(false);

        [MenuItem("Livisor/MR Dive/実機設定を修正", false, 41)]
        public static void Fix()
        {
            // batchmode では Unity がダイアログを出せず必ずキャンセル扱いになるので、
            // CLI / CI から実行できるよう承認扱いにする。
            bool ok = Application.isBatchMode || EditorUtility.DisplayDialog(
                "MR Dive — 実機設定を修正",
                "Quest でパススルーを出すために、以下を書き換えます。\n\n" +
                "・Meta の Project Config（パススルー対応 / ローディング画面）\n" +
                "・Android のターゲットアーキテクチャを ARM64 に\n" +
                "・スプラッシュスクリーンを無効化\n\n" +
                "OpenXR の Meta XR Feature は変更せず、状態の報告だけ行います。\n" +
                "続けますか？",
                "修正する", "やめる");

            if (ok) Run(true);
        }

        static void Run(bool apply)
        {
            var report = new Report();

            CheckBuildTarget(report);
            CheckProjectConfig(report, apply);
            CheckArchitecture(report, apply);
            CheckSplash(report, apply);
            CheckOpenXrFeature(report);

            if (apply) AssetDatabase.SaveAssets();

            report.Emit(apply);
        }

        // ------------------------------------------------------------------
        // 各点検
        // ------------------------------------------------------------------

        static void CheckBuildTarget(Report report)
        {
            var target = EditorUserBuildSettings.activeBuildTarget;
            if (target == BuildTarget.Android)
            {
                report.Ok($"ビルドターゲットは Android");
                return;
            }

            // ここは勝手に切り替えない。切り替えに数分かかるうえ、意図せず作業を止めてしまう。
            report.Warn(
                $"ビルドターゲットが {target} です。Quest 向けには File > Build Settings で Android に切り替えてください" +
                "（切り替え後にもう一度この点検を実行すること）");
        }

        static void CheckProjectConfig(Report report, bool apply)
        {
            Type configType = FindType(ProjectConfigType);
            if (configType == null)
            {
                report.Error($"{ProjectConfigType} 型が見つかりません。Meta XR SDK が未導入か、まだインポート中です");
                return;
            }

            object config;
            try
            {
                var cached = configType.GetProperty("CachedProjectConfig",
                    BindingFlags.Public | BindingFlags.Static);
                config = cached?.GetValue(null);
            }
            catch (Exception e)
            {
                report.Error($"Project Config の取得に失敗: {e.Message}");
                return;
            }

            if (config == null)
            {
                report.Error("Project Config を取得できませんでした（CachedProjectConfig が null）");
                return;
            }

            bool dirty = false;

            // --- パススルー対応。これが None だと AndroidManifest に PASSTHROUGH が入らない ---
            dirty |= EnsureEnum(
                report, config, configType,
                memberName: "insightPassthroughSupport",
                desiredNames: new[] { "Supported", "Required" },
                acceptableNames: new[] { "Supported", "Required" },
                label: "パススルー対応 (insightPassthroughSupport)",
                critical: true,
                apply: apply);

            // --- 起動時のローディング画面。黒だと没入が切れるので Meta 側も必須扱いにしている ---
            dirty |= EnsureEnum(
                report, config, configType,
                memberName: "systemLoadingScreenBackground",
                desiredNames: new[] { "ContextualPassthrough" },
                acceptableNames: new[] { "ContextualPassthrough" },
                label: "起動時ローディング画面 (systemLoadingScreenBackground)",
                critical: false,
                apply: apply);

            if (!dirty || !apply) return;

            try
            {
                var commit = configType.GetMethod("CommitProjectConfig",
                    BindingFlags.Public | BindingFlags.Static);
                commit?.Invoke(null, new[] { config });

                // CommitProjectConfig は SetDirty するだけでディスクに書かない。
                // Editor を再起動したりバッチビルドしたりすると消えるので、明示的に保存する。
                var asset = config as UnityEngine.Object;
                if (asset != null) EditorUtility.SetDirty(asset);
            }
            catch (Exception e)
            {
                report.Error($"Project Config の保存に失敗: {e.Message}");
            }
        }

        static void CheckArchitecture(Report report, bool apply)
        {
            var current = PlayerSettings.Android.targetArchitectures;

            if (current == AndroidArchitecture.ARM64)
            {
                report.Ok("Android ターゲットアーキテクチャは ARM64");
                return;
            }

            if (!apply)
            {
                report.Error($"Android ターゲットアーキテクチャが {current} です。Quest では ARM64 のみが必要（パススルーの必須要件）");
                return;
            }

            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            report.Fixed($"Android ターゲットアーキテクチャを {current} → ARM64 に変更しました");
        }

        static void CheckSplash(Report report, bool apply)
        {
            bool splashShown = PlayerSettings.SplashScreen.show;
            bool vrSplashSet = PlayerSettings.virtualRealitySplashScreen != null;

            if (!splashShown && !vrSplashSet)
            {
                report.Ok("スプラッシュスクリーンは無効");
                return;
            }

            if (!apply)
            {
                report.Warn("スプラッシュスクリーンが有効です。MR では起動時に黒画面が挟まって没入が切れるため、切ることを推奨");
                return;
            }

            if (splashShown) PlayerSettings.SplashScreen.show = false;
            if (vrSplashSet) PlayerSettings.virtualRealitySplashScreen = null;
            report.Fixed("スプラッシュスクリーンを無効にしました");
        }

        static void CheckOpenXrFeature(Report report)
        {
            // OpenXR の feature 構成は他の機能と絡むので、勝手に書き換えず状態だけ見る。
            Type helpers = FindType("UnityEditor.XR.OpenXR.Features.FeatureHelpers");
            if (helpers == null)
            {
                report.Warn("OpenXR Plugin の Editor API が見つからず、Meta XR Feature の状態を確認できませんでした");
                return;
            }

            try
            {
                var method = helpers.GetMethod("GetFeatureWithIdForBuildTarget",
                    BindingFlags.Public | BindingFlags.Static);
                if (method == null)
                {
                    report.Warn("GetFeatureWithIdForBuildTarget が見つかりませんでした（OpenXR Plugin のバージョン差）");
                    return;
                }

                object feature = method.Invoke(null, new object[] { BuildTargetGroup.Android, MetaXRFeatureId });
                if (feature == null)
                {
                    report.Error(
                        "OpenXR の Meta XR Feature が Android に登録されていません。" +
                        "Project Settings > XR Plug-in Management > OpenXR (Android) で有効にしてください");
                    return;
                }

                var enabledProp = feature.GetType().GetProperty("enabled",
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
                bool enabled = enabledProp != null && (bool)enabledProp.GetValue(feature);

                if (enabled) report.Ok("OpenXR の Meta XR Feature は有効");
                else
                    report.Error(
                        "OpenXR の Meta XR Feature が無効です。" +
                        "Project Settings > XR Plug-in Management > OpenXR (Android) で有効にしてください");
            }
            catch (Exception e)
            {
                report.Warn($"Meta XR Feature の状態確認に失敗: {e.Message}");
            }
        }

        // ------------------------------------------------------------------
        // 小物
        // ------------------------------------------------------------------

        /// <summary>
        /// enum のメンバが望ましい値のどれかになっているか確かめ、apply なら先頭の候補に直す。
        /// enum の数値は SDK のバージョンで変わりうるので、必ず名前で引く。
        /// </summary>
        static bool EnsureEnum(
            Report report,
            object target,
            Type targetType,
            string memberName,
            string[] desiredNames,
            string[] acceptableNames,
            string label,
            bool critical,
            bool apply)
        {
            PropertyInfo property = targetType.GetProperty(memberName,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
            FieldInfo field = property != null
                ? null
                : targetType.GetField(memberName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);

            if (property == null && field == null)
            {
                report.Warn($"{label} が見つかりませんでした（SDK のバージョン差の可能性）");
                return false;
            }

            Type enumType = property != null ? property.PropertyType : field.FieldType;
            if (!enumType.IsEnum)
            {
                report.Warn($"{label} が enum ではありませんでした");
                return false;
            }

            object currentValue;
            try
            {
                currentValue = property != null ? property.GetValue(target) : field.GetValue(target);
            }
            catch (Exception e)
            {
                report.Warn($"{label} の読み取りに失敗: {e.Message}");
                return false;
            }

            string currentName = currentValue?.ToString() ?? "(null)";
            if (Array.IndexOf(acceptableNames, currentName) >= 0)
            {
                report.Ok($"{label} = {currentName}");
                return false;
            }

            if (!apply)
            {
                string message = $"{label} が {currentName} です。{string.Join(" か ", desiredNames)} にしてください";
                if (critical) report.Error(message + "（これが None のままだと実機でパススルーが一切出ません）");
                else report.Warn(message);
                return false;
            }

            object desired = null;
            foreach (string name in desiredNames)
            {
                if (!Enum.IsDefined(enumType, name)) continue;
                desired = Enum.Parse(enumType, name);
                break;
            }

            if (desired == null)
            {
                report.Warn($"{label} に設定できる値が見つかりませんでした（候補: {string.Join(", ", desiredNames)}）");
                return false;
            }

            try
            {
                if (property != null) property.SetValue(target, desired);
                else field.SetValue(target, desired);
            }
            catch (Exception e)
            {
                report.Error($"{label} の設定に失敗: {e.Message}");
                return false;
            }

            report.Fixed($"{label} を {currentName} → {desired} に変更しました");
            return true;
        }

        static Type FindType(string name)
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type t;
                try { t = assemblies[i].GetType(name, false); }
                catch (Exception) { continue; }
                if (t != null) return t;
            }
            return null;
        }

        /// <summary>点検結果をためて、最後にまとめて出す。</summary>
        sealed class Report
        {
            readonly List<string> _ok = new List<string>();
            readonly List<string> _fixed = new List<string>();
            readonly List<string> _warn = new List<string>();
            readonly List<string> _error = new List<string>();

            public void Ok(string message) => _ok.Add(message);
            public void Fixed(string message) => _fixed.Add(message);
            public void Warn(string message) => _warn.Add(message);
            public void Error(string message) => _error.Add(message);

            public void Emit(bool applied)
            {
                var sb = new StringBuilder();
                sb.AppendLine(applied ? "[MRDive] 実機設定の修正結果" : "[MRDive] 実機設定の点検結果");

                Append(sb, "✅", _ok);
                Append(sb, "🔧 直した", _fixed);
                Append(sb, "⚠️ 推奨", _warn);
                Append(sb, "❌ 要対応", _error);

                if (_error.Count > 0) Debug.LogError(sb.ToString());
                else if (_warn.Count > 0) Debug.LogWarning(sb.ToString());
                else Debug.Log(sb.ToString());

                string summary = _error.Count > 0
                    ? $"要対応 {_error.Count} 件。Console を確認してください。"
                    : _warn.Count > 0
                        ? $"必須項目はすべて満たしています。推奨 {_warn.Count} 件は Console を確認してください。"
                        : "実機に持っていける状態です。";

                if (_fixed.Count > 0) summary = $"{_fixed.Count} 件を修正しました。\n\n{summary}";

                // batchmode ではダイアログを出せないので、ログだけにする。
                if (!Application.isBatchMode)
                    EditorUtility.DisplayDialog("MR Dive — 実機設定", summary, "OK");
            }

            static void Append(StringBuilder sb, string prefix, List<string> items)
            {
                for (int i = 0; i < items.Count; i++) sb.AppendLine($"  {prefix} {items[i]}");
            }
        }
    }
}
