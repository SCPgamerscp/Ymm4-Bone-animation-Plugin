using System;

namespace Ymm4BoneAnimationPlugin.Core.Kinematics
{
    /// <summary>
    /// 口パク・目パチ・手動指定から、そのフレームで表示する画像スロットを決定する。
    /// </summary>
    public static class SlotSelector
    {
        /// <summary>
        /// 表示すべき画像スロットの添字を返す。該当なしの場合は0（先頭スロット）。
        /// 口パクの開き具合に応じて縦スケール補正を <paramref name="pose"/> へ加える。
        /// </summary>
        public static int Select(BoneDefinition bone, EvaluationContext context, ref BonePose pose)
        {
            if (bone is null)
                return 0;

            // 1. 手動指定が最優先
            if (context.ManualSlotSelection != null
                && context.ManualSlotSelection.TryGetValue(bone.Id, out var manualIndex))
            {
                return ClampIndex(bone, manualIndex);
            }

            // 2. 口パク連動
            if (bone.LipSync is { } lipSync)
            {
                var openness = MathHelper.Clamp01((float)context.LipSyncValue);

                if (lipSync.ScaleInfluence != 0f)
                    pose.ScaleY *= 1f + openness * lipSync.ScaleInfluence;

                if (lipSync.SlotNames.Count > 0)
                {
                    // SlotNames は 開→閉 の順。開き具合が大きいほど先頭側を選ぶ。
                    var count = lipSync.SlotNames.Count;
                    var position = (1f - openness) * (count - 1);
                    var index = (int)Math.Round(position);
                    var slotName = lipSync.SlotNames[Math.Clamp(index, 0, count - 1)];
                    var resolved = FindSlotByName(bone, slotName);
                    if (resolved >= 0)
                        return resolved;
                }
            }

            // 3. 目パチ連動
            if (context.EnableBlink && bone.Blink is { } blink && blink.SlotNames.Count > 0)
            {
                var closeAmount = GetBlinkAmount(blink, context);
                var count = blink.SlotNames.Count;
                // SlotNames は 開→閉 の順。
                var index = (int)Math.Round(closeAmount * (count - 1));
                var slotName = blink.SlotNames[Math.Clamp(index, 0, count - 1)];
                var resolved = FindSlotByName(bone, slotName);
                if (resolved >= 0)
                    return resolved;
            }

            return 0;
        }

        /// <summary>
        /// 現在時刻におけるまばたきの閉じ具合(0=開, 1=閉)を返す。
        /// シードで揺らぎを与え、機械的な等間隔にならないようにする。
        /// </summary>
        public static float GetBlinkAmount(BlinkSettings blink, EvaluationContext context)
        {
            var interval = Math.Max(0.2f, blink.IntervalSeconds);
            var duration = Math.Max(0.02f, blink.DurationSeconds);
            if (duration >= interval)
                return 1f;

            var time = context.Time;
            if (time < 0)
                return 0f;

            var cycle = (int)(time / interval);
            // シードとサイクル番号から決定的な揺らぎを作る（同じフレームでは常に同じ結果）
            var jitter = PseudoRandom(context.BlinkSeed, cycle) * (interval - duration);
            var blinkStart = cycle * interval + jitter;
            var elapsed = time - blinkStart;

            if (elapsed < 0 || elapsed > duration)
                return 0f;

            // 閉じ→開くの三角波
            var normalized = (float)(elapsed / duration);
            return normalized < 0.5f
                ? normalized * 2f
                : (1f - normalized) * 2f;
        }

        /// <summary>決定的な擬似乱数(0〜1)。</summary>
        static float PseudoRandom(int seed, int index)
        {
            unchecked
            {
                var hash = seed * 73856093 ^ index * 19349663;
                hash = hash ^ hash >> 13;
                hash *= 1274126177;
                hash = hash ^ hash >> 16;
                return (hash & 0x7FFFFFFF) / (float)0x7FFFFFFF;
            }
        }

        /// <summary>スロット名から添字を検索する。見つからない場合は -1。</summary>
        static int FindSlotByName(BoneDefinition bone, string name)
        {
            if (string.IsNullOrEmpty(name))
                return -1;
            for (var i = 0; i < bone.ImageSlots.Count; i++)
            {
                if (string.Equals(bone.ImageSlots[i].Name, name, StringComparison.Ordinal))
                    return i;
            }
            return -1;
        }

        static int ClampIndex(BoneDefinition bone, int index)
        {
            if (bone.ImageSlots.Count == 0)
                return 0;
            return Math.Clamp(index, 0, bone.ImageSlots.Count - 1);
        }
    }
}
