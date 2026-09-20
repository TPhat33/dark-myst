using System;
using System.Collections;
using System.Collections.Generic;
using DarkMyst.Content;
using UnityEngine;
using UnityEngine.Networking;

namespace DarkMyst.Client.Content
{
    /// <summary>
    /// Loads the bundled content pack out of StreamingAssets.
    /// <para>
    /// On Android StreamingAssets lives compressed inside the APK, so System.IO cannot reach
    /// it and every file has to come through UnityWebRequest. iOS and the editor could use
    /// System.IO, but going through the same path everywhere means the loading code is
    /// exercised on desktop too rather than only failing on a device.
    /// </para>
    /// <para>
    /// This is the fallback pack shipped with the build. Live content is downloaded from the
    /// CDN and cached; both end up in the same <see cref="ContentPack"/>.
    /// </para>
    /// </summary>
    public static class StreamingContent
    {
        private const string ContentFolder = "content";

        private static readonly string[] Files =
        {
            "manifest.json", "skills.json", "characters.json",
            "enemies.json", "progression.json", "encounters.json"
        };

        /// <summary>
        /// Reads every content file, then hands the texts to the shared loader. Call
        /// <paramref name="onLoaded"/> is invoked with the pack, or <paramref name="onFailed"/>
        /// with the reason.
        /// </summary>
        public static IEnumerator Load(Action<ContentPack> onLoaded, Action<string> onFailed)
        {
            var texts = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (string file in Files)
            {
                string url = System.IO.Path.Combine(Application.streamingAssetsPath, ContentFolder, file);
                if (!url.Contains("://"))
                {
                    url = "file://" + url;
                }

                using (UnityWebRequest request = UnityWebRequest.Get(url))
                {
                    yield return request.SendWebRequest();

                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        onFailed?.Invoke("Could not read " + file + ": " + request.error);
                        yield break;
                    }

                    texts[file] = request.downloadHandler.text;
                }
            }

            ContentPack pack;
            try
            {
                pack = ContentPack.Load(name => texts.TryGetValue(name, out string text) ? text : null);
            }
            catch (ContentException ex)
            {
                onFailed?.Invoke(ex.Message);
                yield break;
            }

            onLoaded?.Invoke(pack);
        }
    }
}
