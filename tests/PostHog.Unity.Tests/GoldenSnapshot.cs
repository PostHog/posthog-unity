using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PostHogUnity.Tests
{
    static class GoldenSnapshot
    {
        public static void Match(string fileName, JsonNode actual)
        {
            var snapshotPath = FindSnapshotPath(fileName);
            var canonicalActual =
                Canonicalize(actual)
                    .ToJsonString(
                        new JsonSerializerOptions
                        {
                            WriteIndented = true,
                            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                        }
                    ) + Environment.NewLine;

            if (Environment.GetEnvironmentVariable("UPDATE_SNAPSHOTS") == "1")
            {
                File.WriteAllText(snapshotPath, canonicalActual);
                return;
            }

            Assert.True(
                File.Exists(snapshotPath),
                $"Snapshot not found: {snapshotPath}. Run with UPDATE_SNAPSHOTS=1 to create it."
            );
            Assert.Equal(File.ReadAllText(snapshotPath), canonicalActual);
        }

        static string FindSnapshotPath(string fileName)
        {
            var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (directory != null)
            {
                var candidate = Path.Combine(
                    directory.FullName,
                    "tests",
                    "PostHog.Unity.Tests",
                    "Snapshots",
                    fileName
                );
                if (Directory.Exists(Path.GetDirectoryName(candidate)))
                {
                    return candidate;
                }
                directory = directory.Parent;
            }

            return Path.Combine(AppContext.BaseDirectory, "Snapshots", fileName);
        }

        static JsonNode Canonicalize(JsonNode node)
        {
            return node switch
            {
                JsonObject obj => new JsonObject(
                    obj.OrderBy(property => property.Key, StringComparer.Ordinal)
                        .Select(property =>
                            KeyValuePair.Create(
                                property.Key,
                                property.Value == null ? null : Canonicalize(property.Value)
                            )
                        )
                ),
                JsonArray array => new JsonArray(
                    array.Select(item => item == null ? null : Canonicalize(item)).ToArray()
                ),
                _ => node.DeepClone(),
            };
        }
    }
}
