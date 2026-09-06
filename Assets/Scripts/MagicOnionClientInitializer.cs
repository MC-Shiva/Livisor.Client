using Livisor.Shared.Hubs;
using Livisor.Shared.UnaryServices;
using MagicOnion.Client;

/// <summary>
/// IL2CPP（Meta Quest）向けに、MagicOnion のクライアントコードをビルド時に生成させる宣言。
/// Mono（エディタ・Windows）では動的生成で動くため無くても繋がるが、IL2CPP では実行時のコード生成が
/// できないため、この宣言が無いと接続時に落ちる（2026-08-29 議事録 5-7）。
/// 生成は Assets/Packages の MagicOnion.Client.SourceGenerator（アナライザ）が行う。
/// </summary>
[MagicOnionClientGeneration(typeof(ITimelineService), typeof(IRoomStateHub))]
partial class MagicOnionClientInitializer
{
}
