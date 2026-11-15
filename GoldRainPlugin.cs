using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using GoldRain.Configuration;
using GoldRain.Core;
using GoldRain.Network;
using GoldRain.UI;
using UnityEngine;

namespace GoldRain
{
    [BepInPlugin(ModGUID, ModName, ModVersion)]
    public class GoldRainPlugin : BaseUnityPlugin
    {
        public const string ModGUID = "com.unclebug.goldrain";
        public const string ModName = "GoldRain";
        public const string ModVersion = "1.0.0";

        public static GoldRainPlugin Instance { get; private set; } = null!;
        public static ManualLogSource Log => Instance.Logger;

        private Harmony _harmony = null!;

        private void Awake()
        {
            Instance = this;

            // 1. Инициализация параметров конфигурации
            ModConfig.Init(Config);

            // 2. Инициализация и применение хуков Harmony
            _harmony = new Harmony(ModGUID);
            _harmony.PatchAll();

            Log.LogInfo($"[{ModName}] Мод v{ModVersion} успешно загружен и готов к работе.");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }

        private void Update()
        {
            // Если мод выключен в настройках - прекращаем обработку ввода
            if (ModConfig.ModEnabled != null && !ModConfig.ModEnabled.Value)
            {
                var local = Player.m_localPlayer;
                if (local != null)
                {
                    var ctrl = local.GetComponent<PeeController>();
                    if (ctrl != null && ctrl.IsPeeing)
                    {
                        ctrl.SetPeeing(false);
                        PeeNetwork.SendPeeState(local, false);
                    }
                }
                return;
            }

            // Игнорируем ввод, если открыт чат, консоль или другое модальное окно ввода
            if (IsInputBlocked())
            {
                return;
            }

            var player = Player.m_localPlayer;
            if (player == null || player.IsDead()) return;

            var controller = player.GetComponent<PeeController>();
            if (controller == null)
            {
                controller = player.gameObject.AddComponent<PeeController>();
            }

            KeyCode hotkey = ModConfig.PeeHotkey != null ? ModConfig.PeeHotkey.Value : KeyCode.P;
            if (hotkey == KeyCode.None) return;

            // Обработка режимов управления: Toggle (переключение) vs Hold (удержание)
            if (ModConfig.PeeMode != null && ModConfig.PeeMode.Value == PeeMode.Toggle)
            {
                if (Input.GetKeyDown(hotkey))
                {
                    bool nextState = !controller.IsPeeing;
                    controller.SetPeeing(nextState);
                    PeeNetwork.SendPeeState(player, nextState);
                }
            }
            else // PeeMode.Hold
            {
                if (Input.GetKeyDown(hotkey))
                {
                    if (!controller.IsPeeing)
                    {
                        controller.SetPeeing(true);
                        PeeNetwork.SendPeeState(player, true);
                    }
                }
                else if (Input.GetKeyUp(hotkey))
                {
                    if (controller.IsPeeing)
                    {
                        controller.SetPeeing(false);
                        PeeNetwork.SendPeeState(player, false);
                    }
                }
                else if (controller.IsPeeing && !Input.GetKey(hotkey))
                {
                    controller.SetPeeing(false);
                    PeeNetwork.SendPeeState(player, false);
                }
            }
        }

        /// <summary>
        /// Проверяет, заблокирован ли ввод текста/управления интерфейсом игры.
        /// </summary>
        private static bool IsInputBlocked()
        {
            // Открыто внутриигровое окно настроек GoldRain
            if (GoldRainSettingsGUI.IsOpen) return true;

            // Чат активен и принимает текстовый ввод
            if (Chat.instance != null && Chat.instance.HasFocus()) return true;

            // Консоль разработчика открыта
            if (Console.IsVisible()) return true;

            // Ввод текста в интерфейсе Valheim
            if (TextInput.IsVisible()) return true;

            // Главное меню открыто
            if (Menu.IsVisible()) return true;

            return false;
        }

        [HarmonyPatch(typeof(Game), "Start")]
        private static class Game_Start_Patch
        {
            private static void Postfix()
            {
                // Гарантируем инициализацию интерфейса настроек
                GoldRainSettingsGUI.EnsureExists();
            }
        }
    }
}
