using System;
using System.Collections.Generic;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Plugin;

namespace Ymm4BonePlugin.Rendering
{
    /// <summary>
    /// 画像ファイルの読み込み結果をパス単位でキャッシュする。
    /// プレビュー中は毎フレーム Update が呼ばれるため、
    /// 同じ画像を読み直さないようにしてパフォーマンスを保つ。
    /// </summary>
    internal sealed class BoneImageCache : IDisposable
    {
        readonly IGraphicsDevicesAndContext devices;
        readonly Dictionary<string, IImageFileSource?> cache = new(StringComparer.OrdinalIgnoreCase);

        public BoneImageCache(IGraphicsDevicesAndContext devices)
        {
            this.devices = devices ?? throw new ArgumentNullException(nameof(devices));
        }

        /// <summary>
        /// 画像を取得する。読み込みに失敗した場合や未指定の場合はnullを返す。
        /// 失敗した結果もキャッシュし、毎フレーム再試行しないようにする。
        /// </summary>
        public IImageFileSource? Get(string? filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return null;

            if (cache.TryGetValue(filePath, out var cached))
                return cached;

            IImageFileSource? source = null;
            try
            {
                source = ImageFileSourceFactory.Create(devices, filePath);
            }
            catch (Exception)
            {
                // 破損ファイル・非対応形式などで例外が出てもプレビューを止めない
                source = null;
            }

            cache[filePath] = source;
            return source;
        }

        /// <summary>
        /// 使用されていない画像を解放する。
        /// ボーン構成の変更で参照されなくなった画像をメモリから落とす。
        /// </summary>
        public void TrimExcept(ISet<string> keepPaths)
        {
            if (keepPaths is null)
                return;

            var removeKeys = new List<string>();
            foreach (var pair in cache)
            {
                if (!keepPaths.Contains(pair.Key))
                    removeKeys.Add(pair.Key);
            }

            foreach (var key in removeKeys)
            {
                cache[key]?.Dispose();
                cache.Remove(key);
            }
        }

        public void Dispose()
        {
            foreach (var source in cache.Values)
                source?.Dispose();
            cache.Clear();
        }
    }
}
