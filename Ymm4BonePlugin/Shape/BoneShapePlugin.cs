using YukkuriMovieMaker.Plugin.Shape;
using YukkuriMovieMaker.Project;

namespace Ymm4BonePlugin.Shape
{
    /// <summary>
    /// YMM4へ「2Dボーン」図形を登録するプラグインのエントリーポイント。
    /// 図形アイテムの図形種類として選択できるようになる。
    /// </summary>
    public class BoneShapePlugin : IShapePlugin
    {
        /// <summary>YMM4上に表示される図形名。</summary>
        public string Name => "2Dボーンアニメーション";

        /// <summary>
        /// 図形アイテムとしてのexo出力には非対応。
        /// ボーン階層の合成結果はAviUtlの標準フィルタで表現できないため。
        /// </summary>
        public bool IsExoShapeSupported => false;

        /// <summary>マスク系のexo出力にも非対応。</summary>
        public bool IsExoMaskSupported => false;

        /// <summary>図形パラメーターを作成する。</summary>
        /// <param name="sharedData">図形の種類を切り替えたときに設定を復元するための共有データ</param>
        public IShapeParameter CreateShapeParameter(SharedDataStore? sharedData)
            => new BoneShapeParameter(sharedData);
    }
}
