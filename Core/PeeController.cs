using System.Reflection;
using GoldRain.Configuration;
using GoldRain.Network;
using UnityEngine;

namespace GoldRain.Core
{
    /// <summary>
    /// Основной компонент управления механикой мочеиспускания персонажа в Valheim.
    /// Отвечает за:
    /// 1. Процедурную позу в LateUpdate (руки аккуратно сложены вперед к поясу/паху, без выворачивания назад).
    /// 2. Сплошной непрерывный поток мочи через LineRenderer (без раздельных капель) + брызги при контакте.
    /// 3. Пространственный стабильный звук медовухи из бочки брожения (Fermenter tap) с плавным нарастанием и затуханием.
    /// 4. Наложение статуса мокрости (Wet) и золотых капель при попадании на других игроков.
    /// 5. Автоматическое прерывание при движении, прыжке, атаке или уроне.
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
        private LineRenderer? _lineRenderer;
        private ParticleSystem? _particleSystem;
        private ParticleSystemRenderer? _particleRenderer;
        private Material? _streamMaterial;
        private AudioSource? _audioSource;

        // Состояние
        public bool IsPeeing { get; private set; }
        private float _lastHealth;
        private bool _initialized;
        private float _targetVolume;
        private float _lastHitSyncTime;

        // Фиксированная стабильная поза (вычисляется при старте для устранения любого дрожания аниматора)
        private Quaternion _fixedSpineRot;
        private Quaternion _fixedUpperArmL;
        private Quaternion _fixedUpperArmR;
        private Quaternion _fixedLowerArmL;
        private Quaternion _fixedLowerArmR;
        private Quaternion _fixedHandL;
        private Quaternion _fixedHandR;
        private bool _hasFixedPose;
        private float _poseBlend;

        // Плавное прицеливание струи в ограниченном переднем секторе
        private float _currentAimYaw;
        private float _currentAimPitch;

        private static readonly MethodInfo? s_showHandItems = typeof(Humanoid).GetMethod(
            "ShowHandItems",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public
        );

        // Общие процедурные ресурсы для минимизации нагрузки на память
        private static AudioClip? s_fermenterClip;
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
        /// Создает сплошную струю через LineRenderer и систему брызг при соприкосновении.
        /// </summary>
        private void CreateParticleStreamSystem()
        {
            if (_streamEmitterObject != null) return;

            _streamEmitterObject = new GameObject("GoldRain_StreamEmitter");
            _streamEmitterObject.transform.SetParent(transform, false);

            // 1. Материал со сглаженной текстурой жидкого золотистого потока
            Shader shader = Shader.Find("Particles/Standard Unlit")
                         ?? Shader.Find("Sprites/Default")
                         ?? Shader.Find("UI/Default");
            _streamMaterial = new Material(shader)
            {
                name = "GoldRain_PeeStreamMat"
            };
            _streamMaterial.mainTexture = GetOrCreateParticleTexture();

            // 2. Сплошная гладкая струя (LineRenderer)
            _lineRenderer = _streamEmitterObject.AddComponent<LineRenderer>();
            _lineRenderer.useWorldSpace = true;
            _lineRenderer.positionCount = 20;
            _lineRenderer.startWidth = 0.024f;
            _lineRenderer.endWidth = 0.042f;
            _lineRenderer.material = _streamMaterial;
            _lineRenderer.numCapVertices = 4;
            _lineRenderer.numCornerVertices = 4;
            _lineRenderer.alignment = LineAlignment.View;
            _lineRenderer.enabled = false;

            Gradient lineGrad = new Gradient();
            lineGrad.SetKeys(
                new GradientColorKey[] {
                    new GradientColorKey(new Color(1.0f, 0.90f, 0.05f), 0.0f), // #FFE600
                    new GradientColorKey(new Color(1.0f, 0.74f, 0.0f), 0.7f),  // #FFBC00
                    new GradientColorKey(new Color(0.92f, 0.62f, 0.0f), 1.0f)
                },
                new GradientAlphaKey[] {
                    new GradientAlphaKey(0.92f, 0.0f),
                    new GradientAlphaKey(0.85f, 0.7f),
                    new GradientAlphaKey(0.40f, 1.0f)
                }
            );
            _lineRenderer.colorGradient = lineGrad;

            // 3. Брызги на месте падения струи (ParticleSystem)
            _particleSystem = _streamEmitterObject.AddComponent<ParticleSystem>();
            _particleRenderer = _streamEmitterObject.GetComponent<ParticleSystemRenderer>();
            _particleRenderer.material = _streamMaterial;
            _particleRenderer.renderMode = ParticleSystemRenderMode.Billboard;

            var main = _particleSystem.main;
            main.duration = 1.0f;
            main.loop = true;
            main.startLifetime = 0.35f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.1f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.05f);
            main.gravityModifier = 1.2f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 25;
            main.playOnAwake = false;

            var emission = _particleSystem.emission;
            emission.enabled = true;
            emission.rateOverTime = 22f;

            var shape = _particleSystem.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 45f;
            shape.radius = 0.04f;

            var col = _particleSystem.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(lineGrad);
        }

        /// <summary>
        /// Создает 3D аудиосистему со звуком наливающейся медовухи из бочки брожения Valheim.
        /// </summary>
        private void CreateAudioSystem()
        {
            if (_audioSource != null || _streamEmitterObject == null) return;

            _audioSource = _streamEmitterObject.AddComponent<AudioSource>();
            _audioSource.clip = GetFermenterClip() ?? GetOrCreateStreamClip();
            _audioSource.loop = true;
            _audioSource.spatialBlend = 1.0f; // 100% пространственный 3D звук
            _audioSource.minDistance = 0.5f;
            _audioSource.maxDistance = 8.0f;
            _audioSource.rolloffMode = AudioRolloffMode.Linear;
            _audioSource.dopplerLevel = 0f;
            _audioSource.playOnAwake = false;
            _audioSource.volume = 0f;
        }

        private static AudioClip? GetFermenterClip()
        {
            if (s_fermenterClip != null) return s_fermenterClip;

            try
            {
                if (ZNetScene.instance != null)
                {
                    GameObject fermenterPrefab = ZNetScene.instance.GetPrefab("fermenter");
                    if (fermenterPrefab != null)
                    {
                        var comp = fermenterPrefab.GetComponent<Fermenter>();
                        if (comp != null && comp.m_tapEffects != null && comp.m_tapEffects.m_effectPrefabs != null)
                        {
                            foreach (var effect in comp.m_tapEffects.m_effectPrefabs)
                            {
                                if (effect?.m_prefab != null)
                                {
                                    var zsfx = effect.m_prefab.GetComponentInChildren<ZSFX>();
                                    if (zsfx != null && zsfx.m_audioClips != null && zsfx.m_audioClips.Length > 0)
                                    {
                                        s_fermenterClip = zsfx.m_audioClips[0];
                                        return s_fermenterClip;
                                    }

                                    var src = effect.m_prefab.GetComponentInChildren<AudioSource>();
                                    if (src != null && src.clip != null)
                                    {
                                        s_fermenterClip = src.clip;
                                        return s_fermenterClip;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            return null;
        }

        public void SetPeeing(bool state)
        {
            if (IsPeeing == state) return;

            IsPeeing = state;

            if (state)
            {
                EnsureInitialized();

                // 1. Убираем оружие/щиты из рук на пояс/за спину без анимации
                if (_player != null)
                {
                    _player.HideHandItems(onlyRightHand: false, animation: false);
                }

                if (_player != null)
                {
                    _lastHealth = _player.GetHealth();
                }

                // 2. Вычисляем стабильную целевую позу ОДИН РАЗ, исключая шум дыхания и аниматора
                Quaternion spineBase = _spine != null ? _spine.rotation : transform.rotation;
                Quaternion upperLBase = _leftUpperArm != null ? _leftUpperArm.rotation : transform.rotation;
                Quaternion upperRBase = _rightUpperArm != null ? _rightUpperArm.rotation : transform.rotation;
                Quaternion lowerLBase = _leftLowerArm != null ? _leftLowerArm.rotation : transform.rotation;
                Quaternion lowerRBase = _rightLowerArm != null ? _rightLowerArm.rotation : transform.rotation;
                Quaternion handLBase = _leftHand != null ? _leftHand.rotation : transform.rotation;
                Quaternion handRBase = _rightHand != null ? _rightHand.rotation : transform.rotation;

                _fixedSpineRot = Quaternion.AngleAxis(-5f, transform.right) * spineBase;
                _fixedUpperArmL = Quaternion.AngleAxis(-28f, transform.right) * Quaternion.AngleAxis(22f, transform.up) * upperLBase;
                _fixedUpperArmR = Quaternion.AngleAxis(-28f, transform.right) * Quaternion.AngleAxis(-22f, transform.up) * upperRBase;
                _fixedLowerArmL = Quaternion.AngleAxis(-65f, transform.right) * Quaternion.AngleAxis(35f, transform.up) * lowerLBase;
                _fixedLowerArmR = Quaternion.AngleAxis(-65f, transform.right) * Quaternion.AngleAxis(-35f, transform.up) * lowerRBase;
                _fixedHandL = Quaternion.AngleAxis(-15f, transform.right) * Quaternion.AngleAxis(25f, transform.up) * handLBase;
                _fixedHandR = Quaternion.AngleAxis(-15f, transform.right) * Quaternion.AngleAxis(-25f, transform.up) * handRBase;

                _hasFixedPose = true;
                _poseBlend = 0f;
                _currentAimYaw = 0f;
                _currentAimPitch = 0f;

                if (_lineRenderer != null)
                {
                    _lineRenderer.enabled = true;
                }

                if (_particleSystem != null)
                {
                    _particleSystem.Play();
                }

                if (_audioSource != null)
                {
                    var clip = GetFermenterClip();
                    if (clip != null && _audioSource.clip != clip)
                    {
                        _audioSource.clip = clip;
                    }
                    _audioSource.loop = true;

                    if (!_audioSource.isPlaying)
                    {
                        _audioSource.volume = 0f;
                        _audioSource.Play();
                    }
                    _targetVolume = (ModConfig.AudioVolume != null ? ModConfig.AudioVolume.Value : 0.35f) * 0.20f;
                }

                if (_player != null && _player == Player.m_localPlayer && ModConfig.ShowNotifications != null && ModConfig.ShowNotifications.Value)
                {
                    _player.Message(MessageHud.MessageType.Center, "<color=#FFE600>Облегчение...</color>");
                }
            }
            else
            {
                if (_lineRenderer != null)
                {
                    _lineRenderer.enabled = false;
                }

                if (_particleSystem != null)
                {
                    _particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                }

                _targetVolume = 0f;
                _hasFixedPose = false;
                _poseBlend = 0f;

                // Возвращаем предметы в руки
                RestoreWeapons();

                if (_player != null && _player == Player.m_localPlayer && ModConfig.ShowNotifications != null && ModConfig.ShowNotifications.Value)
                {
                    _player.Message(MessageHud.MessageType.TopLeft, "<color=#FFE600>Готово.</color>");
                }
            }
        }

        private void RestoreWeapons()
        {
            if (_player != null)
            {
                try
                {
                    s_showHandItems?.Invoke(_player, new object[] { false, false });
                }
                catch { }
            }
        }

        public void StopPeeing()
        {
            if (!IsPeeing) return;
            SetPeeing(false);

            if (_player != null && _player == Player.m_localPlayer)
            {
                PeeNetwork.SendPeeState(_player, false);
            }
        }

        private void Update()
        {
            if (IsPeeing)
            {
                _targetVolume = (ModConfig.AudioVolume != null ? ModConfig.AudioVolume.Value : 0.35f) * 0.20f;
            }

            // Плавное нарастание и затухание звука без хлопков, щелчков и спама
            if (_audioSource != null && _audioSource.isPlaying)
            {
                _audioSource.volume = Mathf.MoveTowards(_audioSource.volume, _targetVolume, Time.deltaTime * 3.5f);
                if (_targetVolume <= 0f && _audioSource.volume <= 0.01f)
                {
                    _audioSource.Stop();
                }
            }
        }

        private void LateUpdate()
        {
            if (!IsPeeing) return;

            // 1. Проверка условий авто-прерывания (только для своего персонажа, без ложных сетевых прерываний)
            if (_player != null)
            {
                if (_player.IsDead() || _player.IsSwimming())
                {
                    StopPeeing();
                    return;
                }

                // Прерывания по управлению, урону и перемещению проверяются ТОЛЬКО для локального игрока
                if (_player == Player.m_localPlayer)
                {
                    Vector3 vel = _player.GetVelocity();
                    Vector3 horizontalVel = new Vector3(vel.x, 0f, vel.z);
                    bool hasMovementInput = ZInput.GetButton("Forward") || ZInput.GetButton("Backward") ||
                                           ZInput.GetButton("Left") || ZInput.GetButton("Right");

                    if ((hasMovementInput && horizontalVel.magnitude > 0.6f) || horizontalVel.magnitude > 1.3f || _player.IsRunning())
                    {
                        StopPeeing();
                        return;
                    }

                    // Прыжок или глубокое падение со скалы
                    if (vel.y > 2.8f || vel.y < -4.0f)
                    {
                        StopPeeing();
                        return;
                    }

                    // Боевые действия или перекат
                    if (_player.InAttack() || _player.InDodge())
                    {
                        StopPeeing();
                        return;
                    }

                    // Реальный полученный урон (> 1.0 HP)
                    float currentHealth = _player.GetHealth();
                    if (currentHealth < _lastHealth - 1.0f)
                    {
                        _lastHealth = currentHealth;
                        StopPeeing();
                        return;
                    }
                    _lastHealth = Mathf.Max(_lastHealth, currentHealth);
                }
            }

            // 2. Позиционирование источника струи и расчет угла прицеливания камеры
            UpdateEmitterTransform();

            // 3. Наложение естественной позы (руки удерживают струю без дрожания аниматора)
            ApplyProceduralPose();

            // 4. Расчет сплошной непрерывной струи (LineRenderer) и коллизий с землей/игроками
            UpdateStreamArc();
        }

        /// <summary>
        /// Удерживает руки в естественной позе перед поясом/пахом без выкручивания и дрожания аниматора.
        /// Использует фиксированные при старте целевые ориентации и плавно следует за углом поворота струи.
        /// </summary>
        private void ApplyProceduralPose()
        {
            if (!_hasFixedPose) return;

            // Плавный вход в позу (за ~0.1 сек)
            _poseBlend = Mathf.MoveTowards(_poseBlend, 1f, Time.deltaTime * 10f);

            // Поворот туловища и рук вслед за направлением струи (до ±19 градусов)
            Quaternion aimYawTurn = Quaternion.AngleAxis(_currentAimYaw * 0.35f, transform.up);
            Quaternion aimPitchTilt = Quaternion.AngleAxis(_currentAimPitch * 0.15f, transform.right);
            Quaternion aimPoseRot = aimYawTurn * aimPitchTilt;

            Quaternion targetSpine = aimPoseRot * _fixedSpineRot;
            Quaternion targetUpperL = aimPoseRot * _fixedUpperArmL;
            Quaternion targetUpperR = aimPoseRot * _fixedUpperArmR;
            Quaternion targetLowerL = aimPoseRot * _fixedLowerArmL;
            Quaternion targetLowerR = aimPoseRot * _fixedLowerArmR;
            Quaternion targetHandL = aimPoseRot * _fixedHandL;
            Quaternion targetHandR = aimPoseRot * _fixedHandR;

            if (_poseBlend >= 0.999f)
            {
                if (_spine != null) _spine.rotation = targetSpine;
                if (_leftUpperArm != null) _leftUpperArm.rotation = targetUpperL;
                if (_rightUpperArm != null) _rightUpperArm.rotation = targetUpperR;
                if (_leftLowerArm != null) _leftLowerArm.rotation = targetLowerL;
                if (_rightLowerArm != null) _rightLowerArm.rotation = targetLowerR;
                if (_leftHand != null) _leftHand.rotation = targetHandL;
                if (_rightHand != null) _rightHand.rotation = targetHandR;
            }
            else
            {
                if (_spine != null) _spine.rotation = Quaternion.Slerp(_spine.rotation, targetSpine, _poseBlend);
                if (_leftUpperArm != null) _leftUpperArm.rotation = Quaternion.Slerp(_leftUpperArm.rotation, targetUpperL, _poseBlend);
                if (_rightUpperArm != null) _rightUpperArm.rotation = Quaternion.Slerp(_rightUpperArm.rotation, targetUpperR, _poseBlend);
                if (_leftLowerArm != null) _leftLowerArm.rotation = Quaternion.Slerp(_leftLowerArm.rotation, targetLowerL, _poseBlend);
                if (_rightLowerArm != null) _rightLowerArm.rotation = Quaternion.Slerp(_rightLowerArm.rotation, targetLowerR, _poseBlend);
                if (_leftHand != null) _leftHand.rotation = Quaternion.Slerp(_leftHand.rotation, targetHandL, _poseBlend);
                if (_rightHand != null) _rightHand.rotation = Quaternion.Slerp(_rightHand.rotation, targetHandR, _poseBlend);
            }
        }

        private void UpdateEmitterTransform()
        {
            if (_streamEmitterObject == null) return;

            Vector3 bodyForward = transform.forward;
            Vector3 bodyUp = transform.up;

            // Определяем направление взгляда игрока (камера для локального игрока, m_lookDir для коопа)
            Vector3 lookDir;
            if (_player == Player.m_localPlayer && GameCamera.instance != null)
            {
                lookDir = GameCamera.instance.transform.forward;
            }
            else
            {
                lookDir = _player != null ? _player.GetLookDir() : bodyForward;
            }

            // Горизонтальный азимут
            Vector3 lookHoriz = Vector3.ProjectOnPlane(lookDir, bodyUp).normalized;
            Vector3 bodyHoriz = Vector3.ProjectOnPlane(bodyForward, bodyUp).normalized;

            float yawAngle = 0f;
            if (lookHoriz.sqrMagnitude > 0.001f && bodyHoriz.sqrMagnitude > 0.001f)
            {
                yawAngle = Vector3.SignedAngle(bodyHoriz, lookHoriz, bodyUp);
            }

            // Ограничение: управляемый малый сектор спереди (±55 градусов)
            // Игрок НЕ может писать назад за спину при взгляде назад
            float targetAimYaw = Mathf.Clamp(yawAngle, -55f, 55f);

            // Вертикальный наклон: от -35 градусов (вниз под ноги) до +15 градусов (вдаль)
            float pitchAngle = Mathf.Asin(Mathf.Clamp(lookDir.y, -0.99f, 0.99f)) * Mathf.Rad2Deg;
            float targetAimPitch = Mathf.Clamp(pitchAngle, -35f, 15f);

            // Плавное следование за камерой с комфортной скоростью
            _currentAimYaw = Mathf.MoveTowardsAngle(_currentAimYaw, targetAimYaw, Time.deltaTime * 180f);
            _currentAimPitch = Mathf.MoveTowards(_currentAimPitch, targetAimPitch, Time.deltaTime * 120f);

            // Итоговый угол струи (с добавлением +10 градусов для красивой баллистической дуги)
            float finalPitch = _currentAimPitch + 10f;
            Quaternion aimRotation = Quaternion.LookRotation(bodyHoriz, bodyUp) * Quaternion.Euler(-finalPitch, _currentAimYaw, 0f);

            // Позиция источника струи у паха
            Vector3 aimHorizDir = Quaternion.AngleAxis(_currentAimYaw * 0.35f, bodyUp) * bodyHoriz;
            Vector3 basePos;
            if (_hips != null)
            {
                basePos = _hips.position + aimHorizDir * 0.22f + Vector3.down * 0.05f;
            }
            else
            {
                basePos = transform.position + Vector3.up * 0.85f + aimHorizDir * 0.22f + Vector3.down * 0.05f;
            }

            _streamEmitterObject.transform.position = basePos;
            _streamEmitterObject.transform.rotation = aimRotation;
        }

        /// <summary>
        /// Формирует непрерывную баллистическую дугу струи мочи без раздельных капель.
        /// </summary>
        private void UpdateStreamArc()
        {
            if (_streamEmitterObject == null || _lineRenderer == null) return;

            Vector3 startPos = _streamEmitterObject.transform.position;
            Vector3 forward = _streamEmitterObject.transform.forward;
            float force = ModConfig.StreamForce != null ? ModConfig.StreamForce.Value : 3.0f;
            Vector3 vel = forward * force;

            int count = 20;
            _lineRenderer.positionCount = count;
            Vector3 currentPos = startPos;
            Vector3 gravity = Physics.gravity * 0.95f;
            float dt = 0.045f;

            int layerMask = LayerMask.GetMask("terrain", "Default", "piece", "character", "character_net");
            if (layerMask == 0) layerMask = LayerMask.GetMask("Default") | 1;

            bool hitFound = false;
            Vector3 hitPoint = Vector3.zero;
            Vector3 hitNormal = Vector3.up;
            Collider? hitCollider = null;

            for (int i = 0; i < count; i++)
            {
                if (!hitFound)
                {
                    Vector3 nextPos = currentPos + vel * dt + 0.5f * gravity * (dt * dt);
                    vel += gravity * dt;

                    Vector3 segDir = nextPos - currentPos;
                    float segDist = segDir.magnitude;
                    if (segDist > 0.001f)
                    {
                        RaycastHit[] hits = Physics.RaycastAll(currentPos, segDir.normalized, segDist, layerMask);
                        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
                        foreach (var hit in hits)
                        {
                            // Игнорируем коллайдеры собственного тела/брони
                            if (hit.collider != null && hit.collider.transform.root == transform.root)
                            {
                                continue;
                            }

                            hitFound = true;
                            hitPoint = hit.point;
                            hitNormal = hit.normal;
                            hitCollider = hit.collider;
                            break;
                        }

                        if (hitFound)
                        {
                            _lineRenderer.SetPosition(i, hitPoint);
                            currentPos = hitPoint;
                        }
                        else
                        {
                            _lineRenderer.SetPosition(i, currentPos);
                            currentPos = nextPos;
                        }
                    }
                    else
                    {
                        _lineRenderer.SetPosition(i, currentPos);
                        currentPos = nextPos;
                    }
                }
                else
                {
                    _lineRenderer.SetPosition(i, hitPoint);
                }
            }

            // Анимация непрерывного течения струи вдоль луча
            if (_streamMaterial != null)
            {
                _streamMaterial.mainTextureOffset = new Vector2(-Time.time * 6.5f, 0f);
            }

            // Позиционирование брызг в точке удара о землю
            if (hitFound && _particleSystem != null)
            {
                _particleSystem.transform.position = hitPoint;
                _particleSystem.transform.rotation = Quaternion.LookRotation(hitNormal);
            }

            // Проверка попадания на другого игрока
            if (hitFound && hitCollider != null)
            {
                CheckPeeTargetHit(hitCollider);
            }
        }

        private void CheckPeeTargetHit(Collider col)
        {
            // Проверку попадания струи производит ТОЛЬКО стреляющий владелец (локальный игрок)
            if (_player == null || _player != Player.m_localPlayer) return;

            var targetPlayer = col.GetComponentInParent<Player>();
            if (targetPlayer != null && targetPlayer != _player)
            {
                if (Time.time > _lastHitSyncTime + 1.5f)
                {
                    _lastHitSyncTime = Time.time;
                    PeeNetwork.SendPeeHit(targetPlayer);
                }
                return;
            }

            var character = col.GetComponentInParent<Character>();
            if (character != null && character != _player)
            {
                if (Time.time > _lastHitSyncTime + 1.5f)
                {
                    _lastHitSyncTime = Time.time;
                    var seman = character.GetSEMan();
                    if (seman != null && !seman.HaveStatusEffect("Wet".GetStableHashCode()))
                    {
                        seman.AddStatusEffect("Wet".GetStableHashCode(), resetTime: true);
                    }
                }
            }
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
                b0 = 0.99765f * b0 + white * 0.0990460f;
                b1 = 0.96300f * b1 + white * 0.2965164f;
                b2 = 0.57000f * b2 + white * 1.0526913f;
                float pink = b0 + b1 + b2 + white * 0.1848f;

                float time = (float)i / sampleRate;
                float mod1 = Mathf.Sin(2f * Mathf.PI * 11.2f * time) * 0.18f;
                float mod2 = Mathf.Sin(2f * Mathf.PI * 23.7f * time) * 0.12f;
                float mod3 = Mathf.Cos(2f * Mathf.PI * 47.1f * time) * 0.08f;
                float streamTone = Mathf.Sin(2f * Mathf.PI * 420f * time) * 0.04f;

                float sample = (pink * 0.14f + streamTone) * (1.0f + mod1 + mod2 + mod3);
                data[i] = Mathf.Clamp(sample, -1.0f, 1.0f);
            }

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
                wrapMode = TextureWrapMode.Repeat,
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
