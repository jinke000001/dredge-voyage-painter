using System;
using System.IO;
using System.Linq;

namespace Jinke.DredgeSkins.NativeQA
{
    public static class ProbeOutput
    {
        public static bool HasResultFiles(string directory)
        {
            return Directory.Exists(directory) && Directory.EnumerateFiles(directory).Any(path =>
            {
                string name = Path.GetFileName(path);
                return !name.StartsWith("._", StringComparison.Ordinal) && name != ".DS_Store";
            });
        }
    }
}
