using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using CatiaWingDesigner.Core.Model;

namespace CatiaWingDesigner.Core.Services
{
    public sealed class DesignJsonSerializer
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public void Save(WingDesign design, string path)
        {
            var json = JsonSerializer.Serialize(design, Options);
            File.WriteAllText(path, json);
        }

        public WingDesign Load(string path)
        {
            var json = File.ReadAllText(path);
            var design = JsonSerializer.Deserialize<WingDesign>(json, Options);
            if (design == null)
            {
                throw new InvalidDataException("JSON 文件不是有效的机翼设计。");
            }

            return design;
        }
    }
}
