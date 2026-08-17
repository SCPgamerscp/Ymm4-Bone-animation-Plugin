using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using YukkuriMovieMaker.Commons;
using Ymm4BonePlugin.Core;
using Ymm4BonePlugin.Shape;

namespace Ymm4BonePlugin.Views
{
    /// <summary>
    /// ボーン構造をJSONテンプレートとして保存・読み込みするUI。
    /// </summary>
    public partial class TemplateIoEditor : UserControl, IPropertyEditorControl
    {
        public event EventHandler? BeginEdit;
        public event EventHandler? EndEdit;

        ItemProperty[]? properties;

        const string FileFilter = "ボーンテンプレート (*.json)|*.json|すべてのファイル (*.*)|*.*";

        public TemplateIoEditor()
        {
            InitializeComponent();
        }

        internal void SetProperties(ItemProperty[]? itemProperties)
            => properties = itemProperties;

        /// <summary>編集対象の図形パラメーターを取得する。</summary>
        BoneShapeParameter? GetParameter()
            => properties?.FirstOrDefault()?.PropertyOwner as BoneShapeParameter;

        void Save_Click(object sender, RoutedEventArgs e)
        {
            var parameter = GetParameter();
            if (parameter is null)
            {
                SetStatus("保存対象が見つかりませんでした。");
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = FileFilter,
                FileName = "bone_template.json",
                Title = "ボーンテンプレートの保存",
            };

            if (dialog.ShowDialog() != true)
                return;

            try
            {
                var skeleton = parameter.BuildSkeleton();

                // IKターゲットの初期値をテンプレートへ反映する
                foreach (var bone in skeleton.Bones)
                {
                    var item = parameter.Bones.FirstOrDefault(b => b.Id == bone.Id);
                    if (item?.IsIkEnabled == true && bone.Ik != null)
                    {
                        bone.Ik.Target = new System.Numerics.Vector2(
                            (float)item.IkTargetX.Values[0].Value,
                            (float)item.IkTargetY.Values[0].Value);
                    }
                }

                var name = Path.GetFileNameWithoutExtension(dialog.FileName);
                var json = SkeletonTemplate.FromSkeleton(skeleton, name).ToJson();
                File.WriteAllText(dialog.FileName, json, System.Text.Encoding.UTF8);

                SetStatus($"保存しました: {Path.GetFileName(dialog.FileName)}（{skeleton.Count}ボーン）");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                SetStatus($"保存に失敗しました: {ex.Message}");
            }
        }

        void Load_Click(object sender, RoutedEventArgs e)
        {
            if (properties is null || properties.Length == 0)
            {
                SetStatus("読み込み対象が見つかりませんでした。");
                return;
            }

            var dialog = new OpenFileDialog
            {
                Filter = FileFilter,
                Title = "ボーンテンプレートの読み込み",
            };

            if (dialog.ShowDialog() != true)
                return;

            SkeletonTemplate? template;
            try
            {
                template = SkeletonTemplate.FromJson(File.ReadAllText(dialog.FileName));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                SetStatus($"読み込みに失敗しました: {ex.Message}");
                return;
            }

            if (template is null || template.Bones.Count == 0)
            {
                SetStatus("有効なボーンテンプレートではありません。");
                return;
            }

            var confirm = MessageBox.Show(
                $"現在のボーン構造を、テンプレート「{template.Name}」({template.Bones.Count}ボーン)で置き換えます。よろしいですか？",
                "テンプレートの読み込み",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.OK)
                return;

            ApplyTemplate(template);
            SetStatus($"読み込みました: {template.Name}（{template.Bones.Count}ボーン）");
        }

        /// <summary>テンプレートの内容をボーン一覧へ反映する。</summary>
        void ApplyTemplate(SkeletonTemplate template)
        {
            var parameter = GetParameter();
            if (parameter is null)
                return;

            BeginEdit?.Invoke(this, EventArgs.Empty);

            var bones = template.Bones.Select(ToBoneItem).ToList();
            if (bones.Count == 0)
                bones.Add(new BoneItem("ボーン1"));

            // 複数アイテムを選択している場合、全てのアイテムへ適用する
            foreach (var property in properties!)
            {
                if (property.PropertyOwner is BoneShapeParameter target)
                    target.Bones = [.. bones.Select(b => new BoneItem(b))];
            }

            EndEdit?.Invoke(this, EventArgs.Empty);
        }

        static BoneItem ToBoneItem(BoneTemplate boneTemplate)
        {
            var bone = new BoneItem(boneTemplate.Name, boneTemplate.ParentId ?? string.Empty)
            {
                Id = string.IsNullOrEmpty(boneTemplate.Id) ? Guid.NewGuid().ToString("N") : boneTemplate.Id,
                Length = boneTemplate.Length,
                AnchorX = boneTemplate.AnchorX,
                AnchorY = boneTemplate.AnchorY,
                BaseZOrder = boneTemplate.BaseZOrder,
            };

            if (boneTemplate.ImageSlots.Count > 0)
                bone.ImageSlots = [.. boneTemplate.ImageSlots.Select(s => new BoneImageSlot(s.Name, s.FilePath))];

            if (boneTemplate.Physics is { } physics)
            {
                bone.IsPhysicsEnabled = true;
                bone.Stiffness = physics.Stiffness;
                bone.Damping = physics.Damping;
                bone.Inertia = physics.Inertia;
                bone.Gravity = physics.Gravity;
                bone.AngleLimit = physics.AngleLimit;
            }

            if (boneTemplate.LipSync is { } lipSync)
            {
                bone.IsLipSyncEnabled = true;
                bone.LipSyncSlotNames = string.Join(",", lipSync.SlotNames);
                bone.LipSyncScaleInfluence = lipSync.ScaleInfluence;
            }

            if (boneTemplate.Blink is { } blink)
            {
                bone.IsBlinkEnabled = true;
                bone.BlinkInterval = blink.IntervalSeconds;
                bone.BlinkDuration = blink.DurationSeconds;
                bone.BlinkSlotNames = string.Join(",", blink.SlotNames);
            }

            if (boneTemplate.Ik is { } ik)
            {
                bone.IsIkEnabled = ik.IsEnabled;
                bone.IkChainLength = ik.ChainLength;
                bone.IkFlipBend = ik.FlipBend;
                bone.IkTargetX.Values[0].Value = ik.TargetX;
                bone.IkTargetY.Values[0].Value = ik.TargetY;
            }

            return bone;
        }

        void SetStatus(string message)
            => statusText.Text = message;
    }
}
