using HarmonyLib;
using GoldRain.Core;
using UnityEngine;

namespace GoldRain.Network
{
    /// <summary>
    /// Обеспечивает сетевую синхронизацию механики мочеиспускания в Valheim.
    /// Синхронизирует состояние между всеми игроками на выделенных серверах и в P2P-коопе.
    /// Использует RPC_GoldRain_Pee на ZNetView персонажа и флаг ZDO GoldRain_IsPeeing для late-join игроков.
    /// </summary>
    public static class PeeNetwork
    {
        public const string RPC_PEE_NAME = "RPC_GoldRain_Pee";
        public const string RPC_PEE_HIT = "RPC_GoldRain_PeeHit";
        public const string ZDO_PEE_KEY = "GoldRain_IsPeeing";

        public static ZNetView? GetZNetView(Player? player)
        {
            return player != null ? player.GetComponent<ZNetView>() : null;
        }

        /// <summary>
        /// Отправляет сетевое RPC всем клиентам о попадании мочи на другого игрока.
        /// </summary>
        public static void SendPeeHit(Player targetPlayer)
        {
            if (targetPlayer == null) return;
            var local = Player.m_localPlayer;
            if (local == null) return;

            var nview = GetZNetView(local);
            if (nview == null || !nview.IsOwner()) return;

            ZDOID targetID = targetPlayer.GetZDOID();
            nview.InvokeRPC(ZNetView.Everybody, RPC_PEE_HIT, targetID);

            // Применяем локально без задержки
            GoldPeeWetDrip.Apply(targetPlayer);
        }

        /// <summary>
        /// Отправляет сетевое RPC всем клиентам и обновляет флаг в ZDO персонажа.
        /// Вызывается только владельцем объекта (локальным игроком).
        /// </summary>
        /// <param name="player">Целевой игрок</param>
        /// <param name="state">Новое состояние мочеиспускания (true = писать, false = остановить)</param>
        public static void SendPeeState(Player player, bool state)
        {
            var nview = GetZNetView(player);
            if (nview == null) return;

            if (nview.IsOwner())
            {
                nview.InvokeRPC(ZNetView.Everybody, RPC_PEE_NAME, state);

                ZDO? zdo = nview.GetZDO();
                if (zdo != null)
                {
                    zdo.Set(ZDO_PEE_KEY, state);
                }
            }
        }

        /// <summary>
        /// Регистрирует сетевой RPC-метод на ZNetView игрока.
        /// </summary>
        public static void RegisterRPC(Player player)
        {
            var nview = GetZNetView(player);
            if (nview == null) return;

            nview.Register<bool>(RPC_PEE_NAME, (long sender, bool state) =>
            {
                var controller = player.GetComponent<PeeController>();
                if (controller == null)
                {
                    controller = player.gameObject.AddComponent<PeeController>();
                }

                if (controller != null)
                {
                    controller.SetPeeing(state);
                }
            });

            nview.Register<ZDOID>(RPC_PEE_HIT, (long sender, ZDOID targetID) =>
            {
                var targetObj = ZNetScene.instance?.FindInstance(targetID);
                if (targetObj != null)
                {
                    var targetPl = targetObj.GetComponent<Player>();
                    if (targetPl != null)
                    {
                        GoldPeeWetDrip.Apply(targetPl);

                        if (targetPl == Player.m_localPlayer)
                        {
                            MessageHud.instance?.ShowMessage(
                                MessageHud.MessageType.Center,
                                "<color=#FFE600>ᚱ Вас обмочили! (Золотой дождь)</color>"
                            );
                        }
                    }
                }
            });

            // Первичная проверка при спавне / подключении
            CheckLateJoinSync(player);
        }

        /// <summary>
        /// Проверяет ZDO персонажа на предмет late-join синхронизации.
        /// Если игрок только что загрузился в поле зрения и уже писал, запускаем струю.
        /// </summary>
        public static void CheckLateJoinSync(Player player)
        {
            var nview = GetZNetView(player);
            if (nview == null) return;

            // Локальный игрок управляет своим состоянием напрямую через ввод
            if (nview.IsOwner()) return;

            ZDO? zdo = nview.GetZDO();
            if (zdo == null) return;

            bool isPeeingZDO = zdo.GetBool(ZDO_PEE_KEY, false);
            var controller = player.GetComponent<PeeController>();

            if (controller != null)
            {
                if (controller.IsPeeing != isPeeingZDO)
                {
                    controller.SetPeeing(isPeeingZDO);
                }
            }
            else if (isPeeingZDO)
            {
                controller = player.gameObject.AddComponent<PeeController>();
                controller.SetPeeing(true);
            }
        }

        [HarmonyPatch(typeof(Player), "Awake")]
        private static class Player_Awake_Patch
        {
            private static void Postfix(Player __instance)
            {
                RegisterRPC(__instance);
            }
        }

        [HarmonyPatch(typeof(Player), "Update")]
        private static class Player_Update_Patch
        {
            private static void Postfix(Player __instance)
            {
                CheckLateJoinSync(__instance);
            }
        }
    }
}
