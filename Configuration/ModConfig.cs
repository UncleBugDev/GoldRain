using BepInEx.Configuration;
using UnityEngine;

namespace GoldRain.Configuration
{
    public enum PeeMode
    {
        Toggle,
        Hold
    }

    public static class ModConfig
    {
        private static ConfigFile _configFile = null!;

        public static ConfigEntry<bool> ModEnabled { get; private set; } = null!;
        public static ConfigEntry<KeyCode> PeeHotkey { get; private set; } = null!;
        public static ConfigEntry<PeeMode> PeeMode { get; private set; } = null!;
        public static ConfigEntry<float> StreamForce { get; private set; } = null!;
        public static ConfigEntry<float> AudioVolume { get; private set; } = null!;
        public static ConfigEntry<bool> ShowNotifications { get; private set; } = null!;

        public static void Init(ConfigFile config)
        {
            _configFile = config;

            ModEnabled = config.Bind(
                "1. General",
                "ModEnabled",
                true,
                new ConfigDescription("Enable or disable the GoldRain mod completely.")
            );

            PeeHotkey = config.Bind(
                "2. Controls",
                "PeeHotkey",
                KeyCode.P,
                new ConfigDescription("Key used to start or toggle peeing.")
            );

            PeeMode = config.Bind(
                "2. Controls",
                "PeeMode",
                Configuration.PeeMode.Toggle,
                new ConfigDescription("Input behavior mode: Toggle (press once to start/stop) or Hold (keep key pressed).")
            );

            StreamForce = config.Bind(
                "3. Visuals",
                "StreamForce",
                3.0f,
                new ConfigDescription("Force and speed of the golden stream arc.", new AcceptableValueRange<float>(1.0f, 5.0f))
            );

            AudioVolume = config.Bind(
                "4. Audio",
                "AudioVolume",
                0.35f,
                new ConfigDescription("Volume of the 3D streaming sound effect.", new AcceptableValueRange<float>(0.0f, 1.0f))
            );

            ShowNotifications = config.Bind(
                "5. UI",
                "ShowNotifications",
                true,
                new ConfigDescription("Show on-screen HUD status messages when peeing starts or stops.")
            );
        }

        public static void Save()
        {
            if (_configFile != null)
            {
                _configFile.Save();
            }
        }
    }
}
