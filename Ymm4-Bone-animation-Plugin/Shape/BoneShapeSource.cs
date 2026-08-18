using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Vortice.Direct2D1;
using Vortice.Mathematics;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;
using Ymm4BoneAnimationPlugin.Core;
using Ymm4BoneAnimationPlugin.Rendering;
using MathHelper = Ymm4BoneAnimationPlugin.Core.MathHelper;

namespace Ymm4BoneAnimationPlugin.Shape
{
    /// <summary>
    /// ボーン階層に従って各パーツ画像をDirect2Dで合成描画する。
    /// プレビュー上のドラッグ操作用の制御点も提供する（IShapeSource2）。
    /// </summary>
    internal class BoneShapeSource : IShapeSource2
    {
        readonly IGraphicsDevicesAndContext devices;
        readonly BoneShapeParameter parameter;
        readonly BoneImageCache imageCache;
        readonly SkeletonEvaluator evaluator = new();

        readonly ID2D1SolidColorBrush boneBrush;
        readonly ID2D1SolidColorBrush jointBrush;
        readonly ID2D1SolidColorBrush ikBrush;
        readonly ID2D1SolidColorBrush selectedBoneBrush;
        readonly ID2D1SolidColorBrush selectedJointBrush;
        readonly ID2D1SolidColorBrush anchorBrush;

        // 不透明度エフェクトは毎フレーム作り直すとプレビューが重くなるため使い回す
        readonly Vortice.Direct2D1.Effects.Opacity opacityEffect;
        readonly ID2D1Image opacityOutput;

        ID2D1CommandList? commandList;

        int lastFrame = int.MinValue;

        /// <summary>プレビュー上に表示する制御点。</summary>
        public IEnumerable<VideoController> Controllers { get; private set; } = [];

        /// <summary>描画結果。</summary>
        public ID2D1Image Output => commandList
            ?? throw new InvalidOperationException($"{nameof(commandList)}がnullです。事前にUpdateを呼び出す必要があります。");

        public BoneShapeSource(IGraphicsDevicesAndContext devices, BoneShapeParameter parameter)
        {
            this.devices = devices;
            this.parameter = parameter;
            imageCache = new BoneImageCache(devices);

            var dc = devices.DeviceContext;
            // ガイド表示用のブラシはコンストラクタで作成し、使い回す。
            boneBrush = dc.CreateSolidColorBrush(new Color4(0.2f, 0.75f, 1f, 0.75f));
            jointBrush = dc.CreateSolidColorBrush(new Color4(1f, 0.9f, 0.2f, 0.9f));
            ikBrush = dc.CreateSolidColorBrush(new Color4(1f, 0.35f, 0.35f, 0.9f));
            selectedBoneBrush = dc.CreateSolidColorBrush(new Color4(1f, 0.55f, 0f, 0.95f)); // 鮮やかなオレンジ
            selectedJointBrush = dc.CreateSolidColorBrush(new Color4(1f, 0.2f, 0f, 1f));   // 濃いオレンジレッド
            anchorBrush = dc.CreateSolidColorBrush(new Color4(0.2f, 0.95f, 0.4f, 0.95f));   // エメラルドグリーン

            opacityEffect = new Vortice.Direct2D1.Effects.Opacity(dc);
            // EffectからgetしたOutputは必ずDisposeする必要がある。Effect側では解放されない。
            opacityOutput = opacityEffect.Output;
        }

        public void Update(TimelineItemSourceDescription description)
        {
            var frame = description.ItemPosition.Frame;
            var length = description.ItemDuration.Frame;
            var fps = description.FPS;

            // タイムラインをシークした場合は物理演算の内部状態をリセットする。
            // 連続再生でない移動で揺れが暴れるのを防ぐ。
            if (frame != lastFrame + 1)
                evaluator.ResetPhysics();
            lastFrame = frame;

            var items = parameter.Bones;
            var skeleton = parameter.BuildSkeleton();

            // ボーンIDから編集項目を引けるようにする
            var itemMap = new Dictionary<string, BoneItem>(items.Count);
            foreach (var item in items)
                itemMap[item.Id] = item;

            // このフレームのIKターゲット・影響度をボーン定義へ反映する
            foreach (var bone in skeleton.Bones)
            {
                if (bone.Ik is null || !itemMap.TryGetValue(bone.Id, out var item))
                    continue;
                bone.Ik.Target = item.GetIkTarget(frame, length, fps);
                bone.Ik.Weight = item.GetIkWeight(frame, length, fps);
            }

            // 手動の差分選択（口パク・目パチ設定がないボーン向け）
            var manualSlots = new Dictionary<string, int>();
            foreach (var item in items)
            {
                if (!item.IsLipSyncEnabled && !item.IsBlinkEnabled)
                    manualSlots[item.Id] = item.GetSlotIndex(frame, length, fps);
            }

            var rawLipSync = parameter.IsLipSyncEnabled ? GetLipSyncValue(description) : 0.0;
            var scaledLipSync = rawLipSync * (parameter.LipSyncScale / 100.0);

            var context = new EvaluationContext
            {
                Time = fps > 0 ? frame / (double)fps : 0,
                DeltaTime = fps > 0 ? 1.0 / fps : 1.0 / 60.0,
                LipSyncValue = scaledLipSync,
                EnablePhysics = parameter.IsPhysicsEnabled,
                EnableBlink = parameter.IsBlinkEnabled,
                BlinkSeed = parameter.BlinkSeed,
                ManualSlotSelection = manualSlots,
            };

            // 奥→手前の順に並んだ評価結果を得る
            var transforms = evaluator.Evaluate(
                skeleton,
                bone => itemMap.TryGetValue(bone.Id, out var item)
                    ? item.GetPose(frame, length, fps)
                    : BonePose.Identity,
                context);

            DrawBones(transforms);
            UpdateControllers(transforms, itemMap, frame, length, fps);
        }

        /// <summary>評価結果に従って画像を合成描画する。</summary>
        void DrawBones(IReadOnlyList<BoneTransform> transforms)
        {
            var dc = devices.DeviceContext;

            // 前回のCommandListを破棄してから新規作成する
            commandList?.Dispose();
            commandList = dc.CreateCommandList();

            dc.Target = commandList;
            dc.BeginDraw();
            dc.Clear(null);

            var originalTransform = dc.Transform;

            foreach (var transform in transforms)
            {
                var slot = transform.ActiveSlot;
                var image = imageCache.Get(slot?.FilePath);
                if (image is null)
                    continue;

                var size = image.Output.Size;
                var bone = transform.Bone;

                // アンカーポイントを原点へ移動してから、ボーンのワールド行列を適用する
                var anchorOffset = Matrix3x2.CreateTranslation(
                    -size.Width * bone.AnchorPoint.X,
                    -size.Height * bone.AnchorPoint.Y);

                dc.Transform = anchorOffset * transform.World;

                var opacity = Math.Clamp(transform.Opacity, 0f, 1f);
                if (opacity <= 0f)
                    continue;

                // 不透明度が1未満の場合はOpacityエフェクトを経由して描画する
                if (opacity >= 0.999f)
                {
                    dc.DrawImage(image.Output, compositeMode: CompositeMode.SourceOver);
                }
                else
                {
                    opacityEffect.Value = opacity;
                    opacityEffect.SetInput(0, image.Output, true);
                    dc.DrawImage(opacityOutput, compositeMode: CompositeMode.SourceOver);
                }
            }

            // ボーンのガイド表示（画像が未設定でも構造が見えるようにする）。
            // 描画内容はプレビューと出力で共通なので、出力にも含まれる点に注意。
            if (parameter.ShowBoneGuide)
                DrawGuides(dc, transforms);

            dc.Transform = originalTransform;
            dc.EndDraw();
            dc.Target = null;   // Targetは必ずnullに戻す
            commandList.Close(); // EndDrawの後に必ずCloseする

            // Effectが画像を掴んだままにならないよう入力をクリアする
            opacityEffect.SetInput(0, null, true);

            // 参照されなくなった画像を解放する
            var usedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var transform in transforms)
            {
                foreach (var slot in transform.Bone.ImageSlots)
                {
                    if (!string.IsNullOrWhiteSpace(slot.FilePath))
                        usedPaths.Add(slot.FilePath);
                }
            }
            imageCache.TrimExcept(usedPaths);
        }

        /// <summary>ボーンの骨組みを線と点で描画する。</summary>
        void DrawGuides(ID2D1DeviceContext dc, IReadOnlyList<BoneTransform> transforms)
        {
            dc.Transform = Matrix3x2.Identity;

            var selectedId = parameter.SelectedBoneId;

            // まず非選択ボーンを描画し、後から選択中ボーンを手前に重ねて描画する
            foreach (var transform in transforms)
            {
                if (transform.Bone.Id == selectedId)
                    continue;
                DrawSingleBoneGuide(dc, transform, false);
            }

            // 選択ボーンを手前にハイライト描画
            foreach (var transform in transforms)
            {
                if (transform.Bone.Id == selectedId)
                    DrawSingleBoneGuide(dc, transform, true);
            }
        }

        void DrawSingleBoneGuide(ID2D1DeviceContext dc, BoneTransform transform, bool isSelected)
        {
            var origin = transform.Origin;
            var tip = transform.Tip;

            if (!MathHelper.IsFinite(origin) || !MathHelper.IsFinite(tip))
                return;

            var bBrush = isSelected ? selectedBoneBrush : boneBrush;
            var jBrush = isSelected ? selectedJointBrush : jointBrush;
            var lineWidth = isSelected ? 4.5f : 2f;
            var jointRadius = isSelected ? 6.5f : 4f;

            // ボーン本体
            dc.DrawLine(origin, tip, bBrush, lineWidth);

            // 関節（根本）
            dc.FillEllipse(new Ellipse(origin, jointRadius, jointRadius), jBrush);
            if (isSelected)
                dc.DrawEllipse(new Ellipse(origin, jointRadius + 2f, jointRadius + 2f), bBrush, 1.5f);

            // 先端の点は描画せず、ピン位置（根本）のみを描画

            // IKターゲット
            var ik = transform.Bone.Ik;
            if (ik is { IsEnabled: true } && MathHelper.IsFinite(ik.Target))
            {
                dc.DrawEllipse(new Ellipse(ik.Target, 7f, 7f), ikBrush, 2f);
                dc.DrawLine(tip, ik.Target, ikBrush, 1f);
            }
        }

        /// <summary>
        /// 完全パペット変形方式：
        /// プレビュー画面には「現在選択されているパーツのピン（1点のみ）」を表示し、
        /// 画面上に複数の丸が重複して並ばないようにする。
        /// </summary>
        void UpdateControllers(
            IReadOnlyList<BoneTransform> transforms,
            Dictionary<string, BoneItem> itemMap,
            int frame,
            int length,
            int fps)
        {
            var controllers = new List<VideoController>();
            var transformMap = transforms.ToDictionary(t => t.Bone.Id);

            // 選択されているボーン、なければ先頭（ルート）ボーンの1つだけを対象にする
            var targetTransform = transforms.FirstOrDefault(t => t.Bone.Id == parameter.SelectedBoneId)
                                  ?? transforms.FirstOrDefault();

            if (targetTransform != null && itemMap.TryGetValue(targetTransform.Bone.Id, out var item))
            {
                var origin = targetTransform.Origin;
                if (MathHelper.IsFinite(origin))
                {
                    var isRoot = string.IsNullOrEmpty(targetTransform.Bone.ParentId);

                    // 単一のパペットピン（ドラッグした位置へ関節を引っ張る）
                    var puppetPin = new ControllerPoint(
                        new Vector3(origin.X, origin.Y, 0),
                        arg =>
                        {
                            parameter.SelectedBoneId = item.Id;
                            var delta = new Vector2(arg.Delta.X, arg.Delta.Y);

                            if (item.IsIkEnabled)
                            {
                                // IK有効時はIKターゲットをドラッグ位置へ移動
                                item.IkTargetX.AddToEachValues(delta.X);
                                item.IkTargetY.AddToEachValues(delta.Y);
                            }
                            else if (isRoot)
                            {
                                // ルートピン（体）は全体を掴んでドラッグ移動
                                var localDelta = ToLocalDelta(delta, targetTransform);
                                item.X.AddToEachValues(localDelta.X);
                                item.Y.AddToEachValues(localDelta.Y);
                            }
                            else
                            {
                                // 親を持つパーツ（頭・腕など）のピンをドラッグしたときは、親関節を中心にこのパーツ自身を回転
                                if (transformMap.TryGetValue(targetTransform.Bone.ParentId!, out var parentTransform))
                                {
                                    var parentOrigin = parentTransform.Origin;
                                    var currentVec = targetTransform.Origin - parentOrigin;
                                    var targetVec = (targetTransform.Origin + delta) - parentOrigin;

                                    if (currentVec.LengthSquared() > 1f && targetVec.LengthSquared() > 1f)
                                    {
                                        var deltaAngle = MathHelper.DeltaDegrees(
                                            MathHelper.ToDegrees(currentVec),
                                            MathHelper.ToDegrees(targetVec));

                                        if (Math.Abs(deltaAngle) > 0.001f)
                                        {
                                            item.Rotation.AddToEachValues(deltaAngle);
                                        }
                                    }
                                }
                                else
                                {
                                    var localDelta = ToLocalDelta(delta, targetTransform);
                                    item.X.AddToEachValues(localDelta.X);
                                    item.Y.AddToEachValues(localDelta.Y);
                                }
                            }
                        });

                    controllers.Add(new VideoController([puppetPin])
                    {
                        Connection = VideoControllerPointConnection.None,
                    });
                }
            }

            Controllers = controllers;
        }

        /// <summary>
        /// ワールド座標でのドラッグ量を、そのボーンのローカル座標系での移動量へ変換する。
        /// 親が回転・スケールしていても、見た目通りにドラッグできるようにする。
        /// </summary>
        static Vector2 ToLocalDelta(Vector2 worldDelta, BoneTransform transform)
        {
            var world = transform.World;
            // 平行移動成分を除いた回転・スケールのみの行列を逆変換する
            var linear = new Matrix3x2(world.M11, world.M12, world.M21, world.M22, 0, 0);
            var inverted = MathHelper.InvertOrIdentity(linear);
            var local = Vector2.Transform(worldDelta, inverted);
            return MathHelper.IsFinite(local) ? local : Vector2.Zero;
        }

        /// <summary>
        /// YMM4から口パクの開き具合を取得する。
        /// 取得できない場合は0（口を閉じた状態）を返す。
        /// </summary>
        static double GetLipSyncValue(TimelineItemSourceDescription description)
        {
            // YMM4のバージョンによって口パク情報の取得手段が異なるため、
            // リフレクションで安全に探索し、見つからない場合は0を返す。
            try
            {
                var type = description.GetType();
                foreach (var name in new[] { "Kuchipaku", "LipSync", "VoiceVolume" })
                {
                    var property = type.GetProperty(name);
                    if (property?.GetValue(description) is double value)
                        return Math.Clamp(value, 0.0, 1.0);
                }
            }
            catch (Exception)
            {
                // 取得できない場合は口パクなしとして扱う
            }
            return 0.0;
        }

        public void Dispose()
        {
            opacityEffect.SetInput(0, null, true); // Effectの入力は必ずnullに戻す
            opacityOutput.Dispose();               // getしたOutputは必ずDisposeする
            opacityEffect.Dispose();

            commandList?.Dispose();
            imageCache.Dispose();
            boneBrush.Dispose();
            jointBrush.Dispose();
            ikBrush.Dispose();
            selectedBoneBrush.Dispose();
            selectedJointBrush.Dispose();
            anchorBrush.Dispose();
        }
    }
}
