using System;

namespace EasyItchPush.Editor
{
    [Serializable]
    internal struct BuildVersion
    {
        public int Major;
        public int Minor;
        public int Patch;
        public bool IsHotfix;
        public int HotfixNumber;

        public BuildVersion(int major, int minor, int patch, bool isHotfix = false, int hotfixNumber = 1)
        {
            Major = Math.Max(0, major);
            Minor = Math.Max(0, minor);
            Patch = Math.Max(0, patch);
            IsHotfix = isHotfix;
            HotfixNumber = Math.Max(1, hotfixNumber);
        }

        public string ToStringWithPrefix()
        {
            var version = $"v{Major}.{Minor}.{Patch}";
            return IsHotfix ? $"{version}-hotfix{HotfixNumber}" : version;
        }

        public string ToStringWithoutPrefix()
        {
            var version = $"{Major}.{Minor}.{Patch}";
            return IsHotfix ? $"{version}-hotfix{HotfixNumber}" : version;
        }

        public string ToReleaseChannelSuffix()
        {
            return $"v{Major}-{Minor}-{Patch}";
        }

        public int ToVersionCode()
        {
            return Major * 10000 + Minor * 100 + Patch + (IsHotfix ? HotfixNumber : 0);
        }

        public static BuildVersion Default => new BuildVersion(1, 0, 1);

        public static bool TryParse(string value, out BuildVersion version)
        {
            version = Default;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var input = value.Trim();
            if (input.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            {
                input = input.Substring(1);
            }

            var isHotfix = false;
            var hotfixNumber = 1;
            var hotfixIndex = input.IndexOf("-hotfix", StringComparison.OrdinalIgnoreCase);
            if (hotfixIndex >= 0)
            {
                var hotfixText = input.Substring(hotfixIndex + "-hotfix".Length);
                input = input.Substring(0, hotfixIndex);
                isHotfix = true;
                if (!int.TryParse(hotfixText, out hotfixNumber))
                {
                    hotfixNumber = 1;
                }
            }

            var parts = input.Split('.');
            if (parts.Length < 2)
            {
                return false;
            }

            if (!int.TryParse(parts[0], out var major))
            {
                return false;
            }

            if (!int.TryParse(parts[1], out var minor))
            {
                return false;
            }

            var patch = 0;
            if (parts.Length >= 3 && !int.TryParse(parts[2], out patch))
            {
                return false;
            }

            version = new BuildVersion(major, minor, patch, isHotfix, hotfixNumber);
            return true;
        }
    }
}
