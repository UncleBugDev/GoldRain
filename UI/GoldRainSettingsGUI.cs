using System;
using GoldRain.Configuration;
using UnityEngine;

namespace GoldRain.UI
{
    /// <summary>
    /// Внутриигровое окно настроек Gold Rain в аутентичном темном скандинавском стиле Valheim.
    /// Открывается через кнопку в меню ESC или программно.
    /// Предоставляет удобный интерфейс для:
    /// - Включения / отключения мода
    /// - Выбора режима (Toggle vs Hold)
    /// - Переназначения клавиши на любую кнопку клавиатуры или мыши
    /// - Настройки силы струи (баллистики дуги)
    /// - Настройки громкости 3D звука
    /// - Управления экранными оповещениями
    /// </summary>
    public class GoldRainSettingsGUI : MonoBehaviour
    {
        public static GoldRainSettingsGUI? Instance { get; private set; }
        public static bool IsOpen => Instance != null && Instance.m_isOpen;

        private bool m_isOpen;
        private bool m_isRebinding;
        private bool m_justStartedRebinding;
        private Rect m_windowRect;
        private Vector2 m_scrollPosition;

        // Стили и шрифты в стиле Valheim
        private Font? m_valheimFont;
        private GUIStyle? m_windowStyle;
        private GUIStyle? m_headerBannerStyle;
        private GUIStyle? m_subHeaderStyle;
        private GUIStyle? m_buttonStyle;
        private GUIStyle? m_activeButtonStyle;
        private GUIStyle? m_rebindButtonStyle;
        private GUIStyle? m_boxStyle;
        private GUIStyle? m_labelStyle;
        private GUIStyle? m_boldLabelStyle;
        private GUIStyle? m_sliderTrackStyle;
        private GUIStyle? m_sliderThumbStyle;

        // Статический кэш процедурных текстур
        private static Texture2D? s_windowTex;
        private static Texture2D? s_btnNormalTex;
        private static Texture2D? s_btnHoverTex;
        private static Texture2D? s_btnActiveTex;
        private static Texture2D? s_bannerTex;
        private static Texture2D? s_boxTex;
        private static Texture2D? s_sliderTrackTex;
        private static Texture2D? s_sliderThumbTex;

        public static void EnsureExists()
        {
            if (Instance != null) return;

            GameObject guiObj = new GameObject("GoldRain_SettingsGUI");
            DontDestroyOnLoad(guiObj);
            Instance = guiObj.AddComponent<GoldRainSettingsGUI>();
        }

        private void Awake()
        {
            Instance = this;
            float w = 500f;
            float h = Mathf.Min(560f, Screen.height - 40f);
            m_windowRect = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
        }

        private void Update()
        {
            if (!m_isOpen) return;

            // Сбрасываем оси ввода игры, чтобы клики не пробивались в мир или меню позади
            if (!m_isRebinding)
            {
                Input.ResetInputAxes();
            }

            // Удержание курсора мыши разблокированным и видимым
            ZCursor.LockState = CursorLockMode.None;
            ZCursor.Show();

            // Закрытие по ESC или отмена переназначения клавиши
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (m_isRebinding)
                {
                    m_isRebinding = false;
                }
                else
                {
                    Close();
                }
                return;
            }

            // Перехват любой клавиши клавиатуры или кнопки мыши при переназначении
            if (m_isRebinding)
            {
                if (m_justStartedRebinding)
                {
                    m_justStartedRebinding = false;
                    return;
                }

                foreach (KeyCode kc in (KeyCode[])Enum.GetValues(typeof(KeyCode)))
                {
                    if (Input.GetKeyDown(kc) && kc != KeyCode.None)
                    {
                        if (kc != KeyCode.Escape)
                        {
                            ModConfig.PeeHotkey.Value = kc;
                            ModConfig.Save();

                            if (Player.m_localPlayer != null && ModConfig.ShowNotifications.Value)
                            {
                                Player.m_localPlayer.Message(
                                    MessageHud.MessageType.TopLeft,
                                    $"[Gold Rain] Клавиша назначена: <color=#FFE600><b>{FormatKeyCodeName(kc)}</b></color>"
                                );
                            }
                        }
                        m_isRebinding = false;
                        break;
                    }
                }
            }
        }

        public void Toggle()
        {
            if (m_isOpen)
            {
                Close();
            }
            else
            {
                Open();
            }
        }

        public void Open()
        {
            m_isOpen = true;
            m_isRebinding = false;
            m_justStartedRebinding = false;

            float w = 500f;
            float h = Mathf.Min(560f, Screen.height - 40f);
            m_windowRect = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);

            SetMenuInteractable(false);

            ZCursor.LockState = CursorLockMode.None;
            ZCursor.Show();
        }

        public void Close()
        {
            m_isOpen = false;
            m_isRebinding = false;
            ModConfig.Save();

            SetMenuInteractable(true);

            if (Player.m_localPlayer != null && !Menu.IsVisible() && !InventoryGui.IsVisible() && !Minimap.IsOpen())
            {
                ZCursor.LockState = CursorLockMode.Locked;
                ZCursor.Hide();
            }
        }

        private static void SetMenuInteractable(bool state)
        {
            try
            {
                if (Menu.instance != null && Menu.instance.m_menuDialog != null)
                {
                    var cg = Menu.instance.m_menuDialog.GetComponent<CanvasGroup>();
                    if (cg == null)
                    {
                        cg = Menu.instance.m_menuDialog.gameObject.AddComponent<CanvasGroup>();
                    }
                    cg.blocksRaycasts = state;
                    cg.interactable = state;
                }
            }
            catch { }
        }

        private void OnDestroy()
        {
            SetMenuInteractable(true);
        }

        private void OnGUI()
        {
            if (!m_isOpen) return;

            InitStyles();

            GUI.depth = -1000;
            m_windowRect = GUI.ModalWindow(
                748192,
                m_windowRect,
                DrawWindowContent,
                "ᚱ Золотой Дождь — Настройки",
                m_windowStyle
            );
        }

        private void DrawWindowContent(int windowId)
        {
            GUILayout.Space(6);

            // Баннер-подзаголовок
            GUILayout.Label("  Процедурная механика мочеиспускания викинга", m_headerBannerStyle);

            m_scrollPosition = GUILayout.BeginScrollView(m_scrollPosition, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

            // СЕКЦИЯ 1: ОБЩИЕ ПАРАМЕТРЫ
            GUILayout.BeginVertical(m_boxStyle);
            GUILayout.Label("<b>1. Общие настройки</b>", m_subHeaderStyle);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Работа мода:", m_labelStyle, GUILayout.Width(180));
            bool isEnabled = ModConfig.ModEnabled.Value;
            string stateLabel = isEnabled ? "<color=#A0FFA0><b>ВКЛЮЧЕН</b></color>" : "<color=#FFA0A0><b>ОТКЛЮЧЕН</b></color>";
            if (GUILayout.Button($"Мод: {stateLabel}", m_buttonStyle, GUILayout.Height(30)))
            {
                ModConfig.ModEnabled.Value = !isEnabled;
                ModConfig.Save();
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Уведомления на экране:", m_labelStyle, GUILayout.Width(180));
            bool showNotif = ModConfig.ShowNotifications.Value;
            string notifText = showNotif ? "<color=#FFE600>ВКЛ</color>" : "<color=#AAAAAA>ВЫКЛ</color>";
            if (GUILayout.Button(notifText, m_buttonStyle, GUILayout.Height(28)))
            {
                ModConfig.ShowNotifications.Value = !showNotif;
                ModConfig.Save();
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUILayout.Space(8);

            // СЕКЦИЯ 2: РЕЖИМ АКТИВАЦИИ
            GUILayout.BeginVertical(m_boxStyle);
            GUILayout.Label("<b>2. Режим управления струей</b>", m_subHeaderStyle);

            GUILayout.BeginHorizontal();
            bool isToggle = ModConfig.PeeMode.Value == PeeMode.Toggle;

            GUIStyle toggleBtnStyle = isToggle ? m_activeButtonStyle! : m_buttonStyle!;
            if (GUILayout.Button("Переключатель (Toggle)", toggleBtnStyle, GUILayout.Height(32)))
            {
                ModConfig.PeeMode.Value = PeeMode.Toggle;
                ModConfig.Save();
            }

            GUIStyle holdBtnStyle = !isToggle ? m_activeButtonStyle! : m_buttonStyle!;
            if (GUILayout.Button("Удержание (Hold)", holdBtnStyle, GUILayout.Height(32)))
            {
                ModConfig.PeeMode.Value = PeeMode.Hold;
                ModConfig.Save();
            }
            GUILayout.EndHorizontal();

            string modeDesc = isToggle
                ? "<color=#CCCCCC><i>Нажал клавишу — струя полилась, нажал еще раз — остановилась.</i></color>"
                : "<color=#CCCCCC><i>Струя льется только пока вы удерживаете клавишу нажатой.</i></color>";
            GUILayout.Label(modeDesc, m_labelStyle);
            GUILayout.EndVertical();

            GUILayout.Space(8);

            // СЕКЦИЯ 3: НАЗНАЧЕНИЕ КЛАВИШИ
            GUILayout.BeginVertical(m_boxStyle);
            GUILayout.Label("<b>3. Горячая клавиша</b>", m_subHeaderStyle);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Клавиша мочеиспускания:", m_labelStyle, GUILayout.Width(180));

            if (m_isRebinding)
            {
                if (GUILayout.Button("<color=#FFE600><b>Слушаю клавишу... (ESC отмена)</b></color>", m_rebindButtonStyle, GUILayout.Height(34)))
                {
                    m_isRebinding = false;
                }
            }
            else
            {
                string keyName = FormatKeyCodeName(ModConfig.PeeHotkey.Value);
                if (GUILayout.Button($"<b>{keyName}</b>  <color=#FFE600>[Изменить]</color>", m_buttonStyle, GUILayout.Height(34)))
                {
                    m_isRebinding = true;
                    m_justStartedRebinding = true;
                }
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUILayout.Space(8);

            // СЕКЦИЯ 4: БАЛЛИСТИКА И 3D ЗВУК
            GUILayout.BeginVertical(m_boxStyle);
            GUILayout.Label("<b>4. Физика дуги и 3D звук</b>", m_subHeaderStyle);

            // Сила струи (Stream Force)
            float force = ModConfig.StreamForce.Value;
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Сила струи (скорость дуги): <b><color=#FFE600>{force:F1}x</color></b>", m_labelStyle);
            GUILayout.EndHorizontal();

            float newForce = GUILayout.HorizontalSlider(force, 1.0f, 5.0f, m_sliderTrackStyle, m_sliderThumbStyle);
            if (Mathf.Abs(newForce - force) > 0.05f)
            {
                ModConfig.StreamForce.Value = (float)Math.Round(newForce, 1);
            }

            GUILayout.Space(6);

            // Громкость звука (Audio Volume)
            float vol = ModConfig.AudioVolume.Value;
            int volPercent = Mathf.RoundToInt(vol * 100f);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Громкость звука струи: <b><color=#FFE600>{volPercent}%</color></b>", m_labelStyle);
            GUILayout.EndHorizontal();

            float newVol = GUILayout.HorizontalSlider(vol, 0.0f, 1.0f, m_sliderTrackStyle, m_sliderThumbStyle);
            if (Mathf.Abs(newVol - vol) > 0.01f)
            {
                ModConfig.AudioVolume.Value = (float)Math.Round(newVol, 2);
            }
            GUILayout.EndVertical();

            GUILayout.EndScrollView();

            GUILayout.Space(8);

            // НИЖНЯЯ ПАНЕЛЬ С КНОПКАМИ
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Сброс по умолчанию", m_buttonStyle, GUILayout.Width(160), GUILayout.Height(34)))
            {
                ModConfig.ModEnabled.Value = true;
                ModConfig.PeeHotkey.Value = KeyCode.P;
                ModConfig.PeeMode.Value = PeeMode.Toggle;
                ModConfig.StreamForce.Value = 3.0f;
                ModConfig.AudioVolume.Value = 0.6f;
                ModConfig.ShowNotifications.Value = true;
                ModConfig.Save();
            }

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("<b>Закрыть</b>", m_activeButtonStyle, GUILayout.Width(130), GUILayout.Height(34)))
            {
                Close();
            }
            GUILayout.EndHorizontal();

            GUI.DragWindow(new Rect(0, 0, m_windowRect.width, 32));
        }

        private static string FormatKeyCodeName(KeyCode kc)
        {
            switch (kc)
            {
                case KeyCode.Mouse0: return "Левая кнопка мыши (ЛКМ)";
                case KeyCode.Mouse1: return "Правая кнопка мыши (ПКМ)";
                case KeyCode.Mouse2: return "Колёсико мыши (СКМ)";
                case KeyCode.Mouse3: return "Боковая кнопка 3";
                case KeyCode.Mouse4: return "Боковая кнопка 4";
                case KeyCode.Mouse5: return "Боковая кнопка 5";
                case KeyCode.Mouse6: return "Боковая кнопка 6";
                case KeyCode.Alpha0: return "0";
                case KeyCode.Alpha1: return "1";
                case KeyCode.Alpha2: return "2";
                case KeyCode.Alpha3: return "3";
                case KeyCode.Alpha4: return "4";
                case KeyCode.Alpha5: return "5";
                case KeyCode.Alpha6: return "6";
                case KeyCode.Alpha7: return "7";
                case KeyCode.Alpha8: return "8";
                case KeyCode.Alpha9: return "9";
                case KeyCode.Return: return "Enter";
                case KeyCode.KeypadEnter: return "Num Enter";
                case KeyCode.LeftShift: return "Левый Shift";
                case KeyCode.RightShift: return "Правый Shift";
                case KeyCode.LeftControl: return "Левый Ctrl";
                case KeyCode.RightControl: return "Правый Ctrl";
                case KeyCode.LeftAlt: return "Левый Alt";
                case KeyCode.RightAlt: return "Правый Alt";
                case KeyCode.Space: return "Пробел (Space)";
                case KeyCode.Tab: return "Tab";
                case KeyCode.Quote: return "' (русская э)";
                case KeyCode.BackQuote: return "~ (русская ё)";
                case KeyCode.Semicolon: return "; (русская ж)";
                case KeyCode.Comma: return ", (русская б)";
                case KeyCode.Period: return ". (русская ю)";
                case KeyCode.Slash: return "/";
                case KeyCode.Backslash: return "\\";
                case KeyCode.LeftBracket: return "[ (русская х)";
                case KeyCode.RightBracket: return "] (русская ъ)";
                case KeyCode.Minus: return "-";
                case KeyCode.Equals: return "=";
                default: return kc.ToString();
            }
        }

        private void InitStyles()
        {
            if (m_valheimFont == null)
            {
                Font[] allFonts = Resources.FindObjectsOfTypeAll<Font>();
                for (int i = 0; i < allFonts.Length; i++)
                {
                    Font f = allFonts[i];
                    if (f != null && (f.name.Contains("Averia") || f.name.Contains("Norse") || f.name.Contains("Valheim")))
                    {
                        m_valheimFont = f;
                        break;
                    }
                }
            }

            if (m_windowStyle == null)
            {
                m_windowStyle = new GUIStyle(GUI.skin.window)
                {
                    normal = { background = GetVikingWindowTexture(), textColor = new Color(0.95f, 0.8f, 0.45f, 1f) },
                    onNormal = { background = GetVikingWindowTexture(), textColor = new Color(0.95f, 0.8f, 0.45f, 1f) },
                    border = new RectOffset(14, 14, 14, 14),
                    padding = new RectOffset(16, 16, 24, 16),
                    font = m_valheimFont,
                    fontStyle = FontStyle.Bold,
                    fontSize = 15,
                    alignment = TextAnchor.UpperCenter
                };
            }

            if (m_headerBannerStyle == null)
            {
                m_headerBannerStyle = new GUIStyle(GUI.skin.label)
                {
                    normal = { background = GetVikingBannerTexture(), textColor = new Color(1f, 0.88f, 0.52f, 1f) },
                    border = new RectOffset(6, 6, 6, 6),
                    padding = new RectOffset(10, 10, 5, 5),
                    font = m_valheimFont,
                    fontStyle = FontStyle.Bold,
                    fontSize = 12,
                    alignment = TextAnchor.MiddleLeft
                };
            }

            if (m_subHeaderStyle == null)
            {
                m_subHeaderStyle = new GUIStyle(GUI.skin.label)
                {
                    font = m_valheimFont,
                    fontSize = 13,
                    fontStyle = FontStyle.Bold,
                    normal = { textColor = new Color(0.95f, 0.82f, 0.45f, 1f) },
                    richText = true
                };
            }

            if (m_buttonStyle == null)
            {
                m_buttonStyle = new GUIStyle(GUI.skin.button)
                {
                    normal = { background = GetVikingButtonTexture(false, false), textColor = new Color(0.92f, 0.88f, 0.82f, 1f) },
                    hover = { background = GetVikingButtonTexture(true, false), textColor = new Color(1f, 0.95f, 0.6f, 1f) },
                    active = { background = GetVikingButtonTexture(false, true), textColor = Color.white },
                    border = new RectOffset(8, 8, 8, 8),
                    font = m_valheimFont,
                    fontStyle = FontStyle.Bold,
                    fontSize = 12,
                    richText = true,
                    alignment = TextAnchor.MiddleCenter
                };
            }

            if (m_activeButtonStyle == null)
            {
                m_activeButtonStyle = new GUIStyle(m_buttonStyle)
                {
                    normal = { background = GetVikingButtonTexture(true, false), textColor = new Color(1f, 0.92f, 0.45f, 1f) },
                    fontStyle = FontStyle.Bold
                };
            }

            if (m_rebindButtonStyle == null)
            {
                m_rebindButtonStyle = new GUIStyle(m_buttonStyle)
                {
                    normal = { background = GetVikingButtonTexture(true, false), textColor = new Color(1f, 0.9f, 0.2f, 1f) },
                    fontStyle = FontStyle.Bold
                };
            }

            if (m_boxStyle == null)
            {
                m_boxStyle = new GUIStyle(GUI.skin.box)
                {
                    normal = { background = GetVikingBoxTexture() },
                    border = new RectOffset(6, 6, 6, 6),
                    padding = new RectOffset(10, 10, 8, 8)
                };
            }

            if (m_labelStyle == null)
            {
                m_labelStyle = new GUIStyle(GUI.skin.label)
                {
                    richText = true,
                    font = m_valheimFont,
                    fontSize = 12,
                    normal = { textColor = new Color(0.9f, 0.86f, 0.80f, 1f) },
                    alignment = TextAnchor.MiddleLeft
                };
            }

            if (m_boldLabelStyle == null)
            {
                m_boldLabelStyle = new GUIStyle(m_labelStyle)
                {
                    fontStyle = FontStyle.Bold,
                    normal = { textColor = new Color(1f, 0.9f, 0.5f, 1f) }
                };
            }

            if (m_sliderTrackStyle == null)
            {
                m_sliderTrackStyle = new GUIStyle(GUI.skin.horizontalSlider)
                {
                    normal = { background = GetSliderTrackTexture() },
                    fixedHeight = 12,
                    border = new RectOffset(4, 4, 4, 4),
                    margin = new RectOffset(4, 4, 6, 6)
                };
            }

            if (m_sliderThumbStyle == null)
            {
                m_sliderThumbStyle = new GUIStyle(GUI.skin.horizontalSliderThumb)
                {
                    normal = { background = GetSliderThumbTexture() },
                    fixedWidth = 16,
                    fixedHeight = 20,
                    border = new RectOffset(4, 4, 4, 4)
                };
            }
        }

        #region Procedural Textures

        private static Texture2D GetVikingWindowTexture()
        {
            if (s_windowTex != null) return s_windowTex;

            const int size = 64;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            Color32[] pixels = new Color32[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int distLeft = x;
                    int distRight = size - 1 - x;
                    int distBottom = y;
                    int distTop = size - 1 - y;
                    int borderDist = Mathf.Min(Mathf.Min(distLeft, distRight), Mathf.Min(distBottom, distTop));

                    if (borderDist <= 1)
                    {
                        pixels[y * size + x] = new Color32(38, 30, 22, 255);
                    }
                    else if (borderDist == 2 || borderDist == 3)
                    {
                        pixels[y * size + x] = new Color32(205, 164, 88, 255);
                    }
                    else if (borderDist == 4)
                    {
                        pixels[y * size + x] = new Color32(18, 14, 10, 255);
                    }
                    else
                    {
                        byte grain = (byte)((x % 4 == 0) ? 2 : 0);
                        byte vFade = (byte)(Mathf.Clamp((y / (float)size) * 12f, 0f, 15f));
                        byte r = (byte)(22 + grain + vFade);
                        byte g = (byte)(18 + grain + vFade);
                        byte b = (byte)(14 + grain + vFade);
                        pixels[y * size + x] = new Color32(r, g, b, 248);
                    }
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply();
            s_windowTex = tex;
            return tex;
        }

        private static Texture2D GetVikingButtonTexture(bool hover, bool active)
        {
            if (active && s_btnActiveTex != null) return s_btnActiveTex;
            if (hover && s_btnHoverTex != null) return s_btnHoverTex;
            if (!hover && !active && s_btnNormalTex != null) return s_btnNormalTex;

            const int w = 64;
            const int h = 32;
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            Color32[] pixels = new Color32[w * h];

            Color32 borderColor = active
                ? new Color32(229, 184, 92, 255)
                : (hover ? new Color32(255, 215, 68, 255) : new Color32(138, 105, 54, 255));

            Color32 bodyColor = active
                ? new Color32(26, 20, 15, 255)
                : (hover ? new Color32(52, 42, 32, 255) : new Color32(36, 28, 22, 255));

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int borderDist = Mathf.Min(Mathf.Min(x, w - 1 - x), Mathf.Min(y, h - 1 - y));

                    if (borderDist <= 1)
                    {
                        pixels[y * w + x] = borderColor;
                    }
                    else if (borderDist == 2)
                    {
                        pixels[y * w + x] = new Color32(18, 14, 10, 255);
                    }
                    else
                    {
                        pixels[y * w + x] = bodyColor;
                    }
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply();

            if (active) s_btnActiveTex = tex;
            else if (hover) s_btnHoverTex = tex;
            else s_btnNormalTex = tex;

            return tex;
        }

        private static Texture2D GetVikingBannerTexture()
        {
            if (s_bannerTex != null) return s_bannerTex;

            const int w = 64;
            const int h = 28;
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            Color32[] pixels = new Color32[w * h];

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int borderDist = Mathf.Min(Mathf.Min(x, w - 1 - x), Mathf.Min(y, h - 1 - y));
                    if (borderDist <= 1)
                    {
                        pixels[y * w + x] = new Color32(180, 140, 68, 255);
                    }
                    else
                    {
                        pixels[y * w + x] = new Color32(42, 32, 20, 240);
                    }
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply();
            s_bannerTex = tex;
            return tex;
        }

        private static Texture2D GetVikingBoxTexture()
        {
            if (s_boxTex != null) return s_boxTex;

            const int size = 32;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            Color32[] pixels = new Color32[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int borderDist = Mathf.Min(Mathf.Min(x, size - 1 - x), Mathf.Min(y, size - 1 - y));
                    if (borderDist <= 1)
                    {
                        pixels[y * size + x] = new Color32(85, 68, 48, 200);
                    }
                    else
                    {
                        pixels[y * size + x] = new Color32(20, 16, 12, 220);
                    }
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply();
            s_boxTex = tex;
            return tex;
        }

        private static Texture2D GetSliderTrackTexture()
        {
            if (s_sliderTrackTex != null) return s_sliderTrackTex;

            const int w = 32;
            const int h = 12;
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            Color32[] pixels = new Color32[w * h];

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int borderDist = Mathf.Min(Mathf.Min(x, w - 1 - x), Mathf.Min(y, h - 1 - y));
                    if (borderDist == 0)
                    {
                        pixels[y * w + x] = new Color32(100, 80, 50, 255);
                    }
                    else
                    {
                        pixels[y * w + x] = new Color32(14, 12, 10, 240);
                    }
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply();
            s_sliderTrackTex = tex;
            return tex;
        }

        private static Texture2D GetSliderThumbTexture()
        {
            if (s_sliderThumbTex != null) return s_sliderThumbTex;

            const int w = 16;
            const int h = 20;
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            Color32[] pixels = new Color32[w * h];

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int borderDist = Mathf.Min(Mathf.Min(x, w - 1 - x), Mathf.Min(y, h - 1 - y));
                    if (borderDist == 0)
                    {
                        pixels[y * w + x] = new Color32(220, 180, 80, 255);
                    }
                    else if (borderDist == 1)
                    {
                        pixels[y * w + x] = new Color32(160, 120, 50, 255);
                    }
                    else
                    {
                        pixels[y * w + x] = new Color32(50, 40, 28, 255);
                    }
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply();
            s_sliderThumbTex = tex;
            return tex;
        }

        #endregion
    }
}
