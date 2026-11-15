using GoldRain.Configuration;
using GoldRain.Network;
using UnityEngine;

namespace GoldRain.Core
{
    /// <summary>
    /// Основной компонент управления механикой мочеиспускания персонажа в Valheim.
    /// Отвечает за:
    /// 1. Процедурную позу GTA San Andreas в LateUpdate (наклоненный назад торс, руки на поясе/паху).
    /// 2. Физический поток мочи (ParticleSystem с параболической физикой, желтым градиентом, коллизиями).
    /// 3. Пространственный 3D звук струи (AudioSource со сгенерированным аудио-потоком).
    /// 4. Автоматическое прерывание при движении, прыжке, атаке или получении урона.
    /// </summary>
    public class PeeController : MonoBehaviour
    {
        private Player _player = null!;
        private Animator? _animator;

        // Кости персонажа
        private Transform? _hips;
        private Transform? _spine;
        private Transform? _leftUpperArm;
        private Transform? _rightUpperArm;
        private Transform? _leftLowerArm;
        private Transform? _rightLowerArm;
        private Transform? _leftHand;
        private Transform? _rightHand;

        // Процедурные визуальные и аудио системы
        private GameObject? _streamEmitterObject;
        private ParticleSystem? _particleSystem;
        private ParticleSystemRenderer? _particleRenderer;
        private Material? _streamMaterial;
        private AudioSource? _audioSource;

        // Состояние
        public bool IsPeeing { get; private set; }
        private float _lastHealth;
        private bool _initialized;

        // Общие процедурные ресурсы для минимизации нагрузки на память
        private static AudioClip? s_sharedAudioClip;
        private static Texture2D? s_sharedParticleTex;

        private void Awake()
        {
            EnsureInitialized();
        }

        public void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;

            _player = GetComponent<Player>();
            if (_player != null)
            {
                _lastHealth = _player.GetHealth();
                _animator = GetComponentInChildren<Animator>();
            }

            CacheBones();
            CreateParticleStreamSystem();
            CreateAudioSystem();
        }

        private void CacheBones()
        {
            if (_animator == null)
            {
                _animator = GetComponentInChildren<Animator>();
            }

            if (_animator != null && _animator.isHuman)
            {
                if (_hips == null) _hips = _animator.GetBoneTransform(HumanBodyBones.Hips);
                if (_spine == null) _spine = _animator.GetBoneTransform(HumanBodyBones.Spine);
                if (_leftUpperArm == null) _leftUpperArm = _animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                if (_rightUpperArm == null) _rightUpperArm = _animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
                if (_leftLowerArm == null) _leftLowerArm = _animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
                if (_rightLowerArm == null) _rightLowerArm = _animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
                if (_leftHand == null) _leftHand = _animator.GetBoneTransform(HumanBodyBones.LeftHand);
                if (_rightHand == null) _rightHand = _animator.GetBoneTransform(HumanBodyBones.RightHand);
            }

            // Запасной поиск в иерархии для нечеловекоподобных ригов или модифицированных моделей
            if (_hips == null) _hips = FindBoneInChildren(transform, "hips", "pelvis");
            if (_spine == null) _spine = FindBoneInChildren(transform, "spine", "chest");
            if (_leftUpperArm == null) _leftUpperArm = FindBoneInChildren(transform, "arm_l", "leftupperarm", "shoulder_l", "upperarm.l");
            if (_rightUpperArm == null) _rightUpperArm = FindBoneInChildren(transform, "arm_r", "rightupperarm", "shoulder_r", "upperarm.r");
            if (_leftLowerArm == null) _leftLowerArm = FindBoneInChildren(transform, "forearm_l", "leftlowerarm", "elbow_l", "lowerarm.l");
            if (_rightLowerArm == null) _rightLowerArm = FindBoneInChildren(transform, "forearm_r", "rightlowerarm", "elbow_r", "lowerarm.r");
            if (_leftHand == null) _leftHand = FindBoneInChildren(transform, "hand_l", "lefthand", "wrist_l", "hand.l");
            if (_rightHand == null) _rightHand = FindBoneInChildren(transform, "hand_r", "righthand", "wrist_r", "hand.r");
        }

        private static Transform? FindBoneInChildren(Transform root, params string[] names)
        {
            foreach (var name in names)
            {
                var found = FindRecursive(root, name);
                if (found != null) return found;
            }
            return null;
        }

        private static Transform? FindRecursive(Transform current, string targetName)
        {
            if (current.name.ToLowerInvariant().Contains(targetName.ToLowerInvariant()))
            {
                return current;
            }
            for (int i = 0; i < current.childCount; i++)
            {
                var res = FindRecursive(current.GetChild(i), targetName);
                if (res != null) return res;
            }
            return null;
        }

        /// <summary>
        /// Создает процедурную систему частиц для параболического потока мочи.
        /// Оптимизирована: максимум 50 частиц, время жизни 1.0 с, нулевая нагрузка на процессор.
        /// </summary>
        private void CreateParticleStreamSystem()
        {
            if (_streamEmitterObject != null) return;

            _streamEmitterObject = new GameObject("GoldRain_StreamEmitter");
            _streamEmitterObject.transform.SetParent(transform, false);

            _particleSystem = _streamEmitterObject.AddComponent<ParticleSystem>();
            _particleRenderer = _streamEmitterObject.GetComponent<ParticleSystemRenderer>();

            // 1. Материал со сглаженной круглой каплей
            Shader shader = Shader.Find("Particles/Standard Unlit") 
                         ?? Shader.Find("Sprites/Default") 
                         ?? Shader.Find("UI/Default");
            _streamMaterial = new Material(shader)
            {
                name = "GoldRain_PeeStreamMat"
            };
            _streamMaterial.mainTexture = GetOrCreateParticleTexture();
            _particleRenderer.material = _streamMaterial;
            _particleRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            _particleRenderer.alignment = ParticleSystemRenderSpace.View;

            // 2. Основной модуль (Main Module)
            var main = _particleSystem.main;
            main.duration = 1.0f;
            main.loop = true;
            // Уменьшенное время жизни: струя долетает до земли и быстро исчезает
            main.startLifetime = 0.65f;
            float force = (ModConfig.StreamForce != null) ? ModConfig.StreamForce.Value : 3.0f;
            main.startSpeed = force;
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.065f);
            main.gravityModifier = 1.25f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 40;
            main.playOnAwake = false;

            // 3. Эмиссия частиц (Emission Module)
            var emission = _particleSystem.emission;
            emission.enabled = true;
            emission.rateOverTime = 36f;

            // 4. Форма конуса (Shape Module)
            var shape = _particleSystem.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 1.8f;
            shape.radius = 0.012f;

            // 5. Цвет по времени жизни: быстрое затухание прозрачности к концу полета
            var col = _particleSystem.colorOverLifetime;
            col.enabled = true;
            Gradient grad = new Gradient();
            grad.SetKeys(
                new GradientColorKey[] {
                    new GradientColorKey(new Color(1.0f, 0.902f, 0.0f), 0.0f), // #FFE600
                    new GradientColorKey(new Color(1.0f, 0.718f, 0.0f), 0.5f), // #FFB700
                    new GradientColorKey(new Color(0.95f, 0.65f, 0.0f), 1.0f)
                },
                new GradientAlphaKey[] {
                    new GradientAlphaKey(0.92f, 0.0f),
                    new GradientAlphaKey(0.75f, 0.5f),
                    new GradientAlphaKey(0.20f, 0.8f),                         // резкий спад прозрачности
                    new GradientAlphaKey(0.0f, 1.0f)                           // полное исчезновение
                }
            );
            col.color = new ParticleSystem.MinMaxGradient(grad);

            // 6. Размер капель со временем: капли быстро уменьшаются перед исчезновением
            var sol = _particleSystem.sizeOverLifetime;
            sol.enabled = true;
            AnimationCurve sizeCurve = new AnimationCurve();
            sizeCurve.AddKey(0f, 0.7f);
            sizeCurve.AddKey(0.35f, 1.0f);
            sizeCurve.AddKey(0.75f, 0.5f);
            sizeCurve.AddKey(1f, 0.1f);
            sol.size = new ParticleSystem.MinMaxCurve(1.0f, sizeCurve);

            // 7. Коллизии с поверхностью: при касании земли/пола частицы исчезают моментально (lifetimeLoss = 1.0)
            var collision = _particleSystem.collision;
            collision.enabled = true;
            collision.type = ParticleSystemCollisionType.World;
            collision.mode = ParticleSystemCollisionMode.Collision3D;
            int mask = LayerMask.GetMask("terrain", "Default", "piece");
            if (mask == 0)
            {
                mask = LayerMask.GetMask("Default") | 1;
            }
            collision.collidesWith = mask;
            collision.dampen = 1.0f;
            collision.bounce = 0.0f;
            collision.lifetimeLoss = 1.0f; // 100% потеря жизни при первом же контакте с землей или постройкой!
            collision.sendCollisionMessages = false;
        }

        /// <summary>
        /// Создает 3D аудиосистему с процедурно сгенерированным зацикленным звуком струи.
        /// </summary>
        private void CreateAudioSystem()
        {
            if (_audioSource != null || _streamEmitterObject == null) return;

            _audioSource = _streamEmitterObject.AddComponent<AudioSource>();
            _audioSource.clip = GetOrCreateStreamClip();
            _audioSource.loop = true;
            _audioSource.spatialBlend = 1.0f; // 100% пространственный 3D звук
            _audioSource.minDistance = 1.0f;
            _audioSource.maxDistance = 15.0f;
            _audioSource.rolloffMode = AudioRolloffMode.Linear;
            _audioSource.dopplerLevel = 0f;
            _audioSource.playOnAwake = false;
            _audioSource.volume = ModConfig.AudioVolume != null ? ModConfig.AudioVolume.Value : 0.6f;
        }

        /// <summary>
        /// Управляет включением/выключением процесса мочеиспускания.
        /// </summary>
        /// <param name="state">True = начать писать, False = закончить</param>
        public void SetPeeing(bool state)
        {
            if (IsPeeing == state) return;

            IsPeeing = state;

            if (state)
            {
                EnsureInitialized();

                if (_player != null)
                {
                    _lastHealth = _player.GetHealth();
                }

                if (_particleSystem != null)
                {
                    var main = _particleSystem.main;
                    float force = (ModConfig.StreamForce != null) ? ModConfig.StreamForce.Value : 3.0f;
                    main.startSpeed = force;
                    _particleSystem.Play();
                }

                if (_audioSource != null)
                {
                    float vol = (ModConfig.AudioVolume != null) ? ModConfig.AudioVolume.Value : 0.6f;
                    _audioSource.volume = vol;
                    _audioSource.Play();
                }

                if (_player != null && _player == Player.m_localPlayer && ModConfig.ShowNotifications != null && ModConfig.ShowNotifications.Value)
                {
                    _player.Message(MessageHud.MessageType.Center, "ᚱ Облегчение...");
                }
            }
            else
            {
                if (_particleSystem != null)
                {
                    _particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                }

                if (_audioSource != null)
                {
                    _audioSource.Stop();
                }

                if (_player != null && _player == Player.m_localPlayer && ModConfig.ShowNotifications != null && ModConfig.ShowNotifications.Value)
                {
                    _player.Message(MessageHud.MessageType.TopLeft, "ᚱ Готово.");
                }
            }
        }

        /// <summary>
        /// Останавливает мочеиспускание и при необходимости отправляет событие в сеть.
        /// </summary>
        public void StopPeeing()
        {
            if (!IsPeeing) return;
            SetPeeing(false);

            if (_player != null && _player == Player.m_localPlayer)
            {
                PeeNetwork.SendPeeState(_player, false);
            }
        }

        private void LateUpdate()
        {
            if (!IsPeeing) return;

            // 1. Проверка условий авто-прерывания для локального игрока или живого персонажа
            if (_player != null)
            {
                if (_player.IsDead())
                {
                    StopPeeing();
                    return;
                }

                // Движение (скорость > 0.1)
                Vector3 vel = _player.GetVelocity();
                Vector3 horizontalVel = new Vector3(vel.x, 0f, vel.z);
                if (horizontalVel.magnitude > 0.1f)
                {
                    StopPeeing();
                    return;
                }

                // Прыжок или нахождение в воздухе
                if (!_player.IsOnGround() || Mathf.Abs(vel.y) > 0.35f)
                {
                    StopPeeing();
                    return;
                }

                // Атака или перекат
                if (_player.InAttack() || _player.InDodge())
                {
                    StopPeeing();
                    return;
                }

                // Получение урона
                float currentHealth = _player.GetHealth();
                if (currentHealth < _lastHealth - 0.05f)
                {
                    _lastHealth = currentHealth;
                    StopPeeing();
                    return;
                }
                _lastHealth = currentHealth;
            }

            // 2. Наложение процедурной позы викинга (GTA San Andreas)
            ApplyProceduralPose();

            // 3. Позиционирование источника струи мочи
            UpdateEmitterTransform();
        }

        /// <summary>
        /// Накладывает процедурные смещения костей в стиле GTA San Andreas:
        /// Spine отклоняется слегка назад (-6 град), руки складываются вперед и внутрь к паху/поясу.
        /// </summary>
        private void ApplyProceduralPose()
        {
            // Отклонение позвоночника слегка назад (-6 градусов)
            if (_spine != null)
            {
                _spine.rotation = Quaternion.AngleAxis(-6f, transform.right) * _spine.rotation;
            }

            // Плечи/верхняя часть рук поворачиваются вперед и внутрь
            if (_leftUpperArm != null)
            {
                Quaternion rotL = Quaternion.AngleAxis(26f, transform.right) * Quaternion.AngleAxis(20f, transform.up);
                _leftUpperArm.rotation = rotL * _leftUpperArm.rotation;
            }
            if (_rightUpperArm != null)
            {
                Quaternion rotR = Quaternion.AngleAxis(26f, transform.right) * Quaternion.AngleAxis(-20f, transform.up);
                _rightUpperArm.rotation = rotR * _rightUpperArm.rotation;
            }

            // Предплечья поворачиваются вверх и внутрь, смыкаясь в районе паха/пояса
            if (_leftLowerArm != null)
            {
                Quaternion lowerL = Quaternion.AngleAxis(35f, transform.right) * Quaternion.AngleAxis(35f, transform.up);
                _leftLowerArm.rotation = lowerL * _leftLowerArm.rotation;
            }
            if (_rightLowerArm != null)
            {
                Quaternion lowerR = Quaternion.AngleAxis(35f, transform.right) * Quaternion.AngleAxis(-35f, transform.up);
                _rightLowerArm.rotation = lowerR * _rightLowerArm.rotation;
            }

            // Кисти рук над пряжкой пояса
            if (_leftHand != null)
            {
                _leftHand.rotation = Quaternion.AngleAxis(15f, transform.up) * _leftHand.rotation;
            }
            if (_rightHand != null)
            {
                _rightHand.rotation = Quaternion.AngleAxis(-15f, transform.up) * _rightHand.rotation;
            }
        }

        /// <summary>
        /// Обновляет позицию эмитера струи:
        /// Смещение от таза вперед на 0.25м и вниз на 0.05м.
        /// </summary>
        private void UpdateEmitterTransform()
        {
            if (_streamEmitterObject == null) return;

            Vector3 basePos;
            Vector3 forwardDir;

            if (_hips != null)
            {
                basePos = _hips.position + _hips.forward * 0.25f + Vector3.down * 0.05f;
                forwardDir = _hips.forward;
            }
            else
            {
                basePos = transform.position + Vector3.up * 0.85f + transform.forward * 0.25f + Vector3.down * 0.05f;
                forwardDir = transform.forward;
            }

            _streamEmitterObject.transform.position = basePos;
            // Небольшой естественный наклон параболы вниз на 12 градусов
            _streamEmitterObject.transform.rotation = Quaternion.LookRotation(forwardDir, Vector3.up) * Quaternion.Euler(12f, 0f, 0f);
        }

        private static AudioClip GetOrCreateStreamClip()
        {
            if (s_sharedAudioClip != null) return s_sharedAudioClip;

            int sampleRate = 44100;
            float duration = 2.0f;
            int samples = (int)(sampleRate * duration);
            float[] data = new float[samples];

            System.Random rand = new System.Random(1337);
            float b0 = 0f, b1 = 0f, b2 = 0f;

            for (int i = 0; i < samples; i++)
            {
                float white = (float)(rand.NextDouble() * 2.0 - 1.0);

                // Фильтр розового шума
                b0 = 0.99765f * b0 + white * 0.0990460f;
                b1 = 0.96300f * b1 + white * 0.2965164f;
                b2 = 0.57000f * b2 + white * 1.0526913f;
                float pink = b0 + b1 + b2 + white * 0.1848f;

                float time = (float)i / sampleRate;

                // Резонансы и кавитация журчащей струи
                float mod1 = Mathf.Sin(2f * Mathf.PI * 11.2f * time) * 0.18f;
                float mod2 = Mathf.Sin(2f * Mathf.PI * 23.7f * time) * 0.12f;
                float mod3 = Mathf.Cos(2f * Mathf.PI * 47.1f * time) * 0.08f;
                float streamTone = Mathf.Sin(2f * Mathf.PI * 420f * time) * 0.04f;

                float sample = (pink * 0.14f + streamTone) * (1.0f + mod1 + mod2 + mod3);
                data[i] = Mathf.Clamp(sample, -1.0f, 1.0f);
            }

            // Плавное бесшовное перекрестное затухание петли (100 мс)
            int fadeLen = (int)(sampleRate * 0.1f);
            for (int i = 0; i < fadeLen; i++)
            {
                float t = (float)i / fadeLen;
                data[samples - fadeLen + i] = Mathf.Lerp(data[samples - fadeLen + i], data[i], t);
            }

            s_sharedAudioClip = AudioClip.Create("GoldRain_StreamAudio", samples, 1, sampleRate, false);
            s_sharedAudioClip.SetData(data, 0);
            return s_sharedAudioClip;
        }

        private static Texture2D GetOrCreateParticleTexture()
        {
            if (s_sharedParticleTex != null) return s_sharedParticleTex;

            int size = 32;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "GoldRain_DropletTex",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
            float radius = size * 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center) / radius;
                    float alpha = Mathf.Clamp01(1f - dist);
                    alpha = Mathf.SmoothStep(0f, 1f, alpha);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }
            tex.Apply();
            s_sharedParticleTex = tex;
            return s_sharedParticleTex;
        }

        private void OnDestroy()
        {
            if (IsPeeing)
            {
                SetPeeing(false);
            }

            if (_streamEmitterObject != null)
            {
                Destroy(_streamEmitterObject);
            }
        }
    }
}
