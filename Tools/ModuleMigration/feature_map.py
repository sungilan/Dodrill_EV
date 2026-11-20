# 기능 폴더 배정표 (2026-11-20) — 어셈블리 경계는 그대로 두고, 어셈블리 안에서 기능별 폴더로 정리한다.
# 형식: '어셈블리/기능/하위' : [파일 이름(.cs 제외), ...]
# reorg.py 가 이 표대로 git mv(.meta 함께) 한다. 표에 없는 .cs 가 있으면 멈춘다.
MAP = {
    # ───────────────────────── Common (공통 기반) ─────────────────────────
    'Common/Core': ['NetworkObjectFinder', 'UserInfo', 'Define', 'Extentions'],
    'Common/Core/Managers': ['Managers', 'PoolManager', 'Poolable', 'ResourceManager', 'SoundManager'],
    'Common/Platform': ['Utils', 'VrDevice', 'XrDetectSignals', 'PermissionsCheck'],
    'Common/Performance': ['FrameRateSetup', 'MobileRuntimeOptimizer', 'VrGraphicsQuality', 'StartupHitchLogger'],
    'Common/Diagnostics': ['AndroidFileLogger'],
    'Common/Network': ['BuildCompat', 'BuildCompatNotice', 'NetTimeoutGuard', 'AvartarSync'],   # AvartarSync → Avatar 후보
    'Common/Ui': ['VrUi', 'MobileWorkButton', 'MobileQuickButtons', 'ResponsiveCanvasScaler'],
    'Common/Keyboard': ['HangulComposer', 'VRKeyboard', 'VRKeyboardButton'],
    'Common/Keyboard/Hangul': ['ChosungType', 'HanHelper', 'HangulHelper', 'HangulState', 'JongsungType', 'JungsungType'],
    'Common/Keyboard/OnScreen': ['OnScreenKeyboard', 'OnScreenKeyboardButton', 'OnScreenKeyboardInputfield', 'OnScreenKeyboardInputfield1'],
    'Common/SpatialAnchor': ['AnchorManager', 'AnchorPreview', 'PlacementController'],
    'Common/World': ['WorkshopFloorCollider'],   # EV 작업장 좌표 — Content/World 후보

    # ───────────────────────── Interaction (잡기·상호작용 코어) ─────────────────────────
    'Interaction/Grab': ['SyncGrab', 'KinematicGrab', 'LocalNetworkTransform', 'SnapGuide', 'GrabHooks', 'HeldItemOnTop'],
    'Interaction/Hand': ['PcHandGrip'],
    'Interaction/Task': ['InteractionEvents', 'TaskInteractionZone', 'TaskItem'],

    # ───────────────────────── Avatar (플레이어·아바타) ─────────────────────────
    'Avatar/Player': ['AvatarHooks', 'BasePlayer', 'PlayerSpawnner', 'AvatarBodyFit', 'HeldObjectHandAttach'],
    'Avatar/PcMobile': ['PlayerCollision', 'FreeLookControllerLocal', 'IPcLookController', 'PcArmReach', 'PcAvatarAnimParams',
                        'PcAvatarIKDriver', 'PcCrouchIK', 'PcHeadAim', 'PcLocomotionAnimator', 'PcMobileAvatarController'],
    'Avatar/Vr': ['VRPlayer', 'VrLeftLaser'],

    # ───────────────────────── Engine (시나리오 엔진) ─────────────────────────
    'Engine/Scenario/Data': ['ScenarioData', 'TaskState', 'ScenarioDataBroadcast', 'ScenarioEndBroadcast', 'FishNetScenarioSerializers', 'ScenarioLoader'],
    'Engine/Scenario/Runtime': ['ScenarioRunner', 'ScenarioDistributor', 'ScenarioBootstrapper', 'ScenarioSpawner', 'SpawnPoint', 'SceneGuideRegistry', 'TransformChangeTracker'],
    'Engine/Scenario/Client': ['ScenarioReceiver', 'ScenarioStateReceiver'],
    'Engine/Scenario/Progress': ['ScenarioProgressManager', 'ScenarioProgressData'],
    'Engine/Scenario/Hooks': ['ScenarioHooks', 'EngineInteractionBinding'],
    'Engine/Modules': ['ITaskModule', 'ModuleRegistry', 'SequentialStepModule', 'NetworkInteractionBridge'],
    'Engine/Modules/Base': ['ConfirmTaskModule', 'GrabAllItemsModule', 'GrabUseTaskModule', 'GrabZoneTaskModule'],
    'Engine/Session': ['SessionPlayerServer', 'SessionPlayersBroadcasts'],
    'Engine/Dialogue': ['MentorDialogue', 'ScenarioDialogueServerMerge', 'ScenarioVoiceLoader', 'TTSManager'],
    'Engine/Evaluation': ['EvaluationBroadcasts', 'EvaluationMode', 'EvaluationResultExporter', 'EvaluationRules', 'EvaluationService'],
    'Engine/Debug': ['DebugBootstrapper', 'DebugCommandBroadcast', 'DebugCommandRegistry', 'DebugCommands', 'DebugConsoleUI', 'DebugLogger', 'IDebugCommand', 'DemoTaskUI'],
    'Engine/Localization': ['LocalizationManager'],
    'Engine/Util': ['KeepWorldRotation'],
    'Engine/Legacy': ['MultiStepModules', 'FireExtinguishModule_Example', 'TaskObjectSpawner', 'ItemPrefabSetup'],   # 간호술기·예시·폐기 예정

    # ───────────────────────── Solo ─────────────────────────
    'Solo': ['SoloModeLauncher'],

    # ───────────────────────── Content (EV 콘텐츠) ─────────────────────────
    # 앱 조립·시작
    'Content/App/Bindings': ['EvScenarioBinding', 'EvAvatarBinding', 'EvInteractionBinding', 'NetworkObjectFinderEv'],
    'Content/App': ['GameScenePlatformManager', 'GameLoadCurtain', 'StartupManager', 'SessionData', 'GameModeManager', 'DemoSettings', 'ScenarioAutoLoader'],
    'Content/App/Demo': ['DemoNetworkStarter', 'StandaloneScenarioRunner'],
    # 로비·메뉴
    'Content/Lobby': ['ClientState', 'CreateNewLobbyPanel', 'DefaultFactoriesModule', 'LobbyBottomPanel', 'LobbyListPanel', 'LobbyPanel', 'LobbyPlayerInfos',
                      'LoginPanel', 'MasterReconnectGuard', 'MatchView', 'MyLobbyFactoryModule', 'PlatformDetector'],
    'Content/Menu': ['StartPanel', 'ModeSelectUI', 'LobbyModeSelector', 'UIManager', 'SettingsPanel'],
    'Content/Menu/ScenarioList': ['ScenarioDataLoader', 'ScenarioListCardUI', 'ScenarioListPanel'],
    'Content/Menu/Language': ['LanguageButtonManager', 'LanguageSelectionPanel', 'Panel_LanguageToggle'],
    'Content/Menu/Loading': ['LoadingBgFakeLoading', 'LoadingBgVisibilityBinder', 'PersistentClientLoadingRoot'],
    'Content/Menu/Popups': ['Popup_EndGame', 'Popup_ExitPanel', 'Popup_LogOutPanel', 'Popup_ScenarioEndPanel', 'ScenarioExitHandler'],
    # 입력
    'Content/Input/Pc': ['FreeLookController', 'KeyboardVisualizer'],
    'Content/Input/Mobile': ['FreeLockControllerMobile', 'MobileHeightButtons'],
    'Content/Input/Shared': ['ClickRay'],
    # 시나리오 모듈(EV)
    'Content/Scenario/Ev': ['EVBaseModules', 'EV12VModules', 'EVAirtightModules', 'EVCoolantModules', 'EVModules', 'EVFreeModules', 'ConfirmTaskZone', 'CheckpointManager'],
    'Content/Legacy/Nursing': ['NursingModuleSet', 'BSTModules', 'EKGModules', 'HandHygieneModule', 'HypoglycemiaTreatmentModules', 'IMInjectionModules',
                               'OralMedicationModules', 'PatientAssessmentModules', 'VitalSignsModules'],
    # 안내(가이드)
    'Content/Guide': ['GuideSystem', 'GuideMarker', 'GuideZoneData', 'NextActionGuide', 'AIGuideManager', 'GhostManager', 'NoneHighlight', 'TaskDirectionCompass'],
    'Content/Guide/View': ['FocusMode', 'XRayMode'],
    # 화면 표시(HUD)
    'Content/Hud': ['PcHudLayout', 'ScreenToast', 'WarningPopup', 'CountdownUI', 'DischargeTimerUI', 'InteractionProgressBarUI', 'TaskTimeWarning',
                    'HeldItemUI', 'ItemNameTag', 'WorldHoverLabel', 'HoverNameFormatter', 'PlayerUISetup', 'StepPopupToggle'],
    'Content/Hud/Task': ['ProgressHUD', 'TaskItemUI', 'TaskListPanel', 'TaskProgressUI'],
    'Content/Hud/Session': ['NetworkedSessionUserList', 'UserStateRow', 'SessionNicknameSender'],
    'Content/Ui/Widgets': ['ClickButton', 'ClickDropdown', 'CustomInputField', 'SimpleTogglePanelUI', 'ToggleButton', 'PopupWorldSpaceAnchor', 'BillboardUI', 'SlidingPanel'],
    # VR 전용
    'Content/Vr/Ui': ['VrPalmMenu', 'VrToastFollow', 'VrFingerUiButtonDriver', 'VrTaskListToggle', 'VrUiButtonFingerFeedback', 'VrUiFingerBindings', 'VrUiMasterVolumeToggle',
                      'VRUIFollower', 'VRUiMetaRepositionDrag'],
    'Content/Vr/Hints': ['VrControlHint', 'VrControllerHints', 'VrGrabHint'],
    'Content/Vr/Interaction': ['VrHaptics', 'VrPlacementGhost'],
    'Content/Vr/Tutorial': ['VrPracticeTutorial'],
    'Content/Vr/Mode': ['MRManager'],
    # 안전·보호구
    'Content/Safety/Ppe': ['PlayerEquipmentSync', 'GloveItem', 'InsulationMat'],
    'Content/Safety/HighVoltage': ['ElectricSafetyManager', 'LOTOSystem', 'HvTouchWarning', 'HvVoltageCheck', 'FreeLoto', 'SafetyUIHandler'],
    'Content/Safety/Accident': ['EmergencyEventManager', 'AccidentFx', 'CarPartAccident'],
    # 평가
    'Content/Evaluation': ['EvaluationClientUI', 'EvaluationReport', 'DataPersistenceManager'],
    # 측정(멀티미터·메거)
    'Content/Measure': ['MultimeterMaster', 'MultimeterDial', 'MultimeterProbe', 'MultimeterProbeSync', 'PCMultimeterManager', 'MeasurementPoint', 'MeasurableNode',
                        'MeasurementLog', 'MobileMeterReadout', 'HvTestTerminals', 'FreeModeTestBoard'],
    'Content/Measure/Probe': ['PcProbeAim', 'VrProbeHandling'],
    # 공구·장비
    'Content/Tools/Core': ['EquipFx', 'EquipmentBase', 'EquipmentFxLibrary', 'EquipmentSlot', 'HeldItemOnTopFeature', 'HeldOutlineSuppressor', 'HoldScalePolicy', 'StayOnRelease', 'ToolRenderSnap'],
    'Content/Tools/Hand': ['ImpactWrench', 'AirImpactWrench', 'TorqueWrench', 'ManualTool', 'SpecialTool', 'SprayTool', 'PaintGun', 'PaintSwatch', 'GreaseTool', 'AngleGrinder',
                           'TapeMeasure', 'UtilityKnife', 'WorkLight', 'InsulationTape'],
    'Content/Tools/Workshop': ['AirCompressor', 'CarryBasket', 'ConeStack', 'CreeperRide', 'FloorDragProp', 'LadderProp', 'LiquidGauge', 'PourContainer', 'ShopCrane', 'SocketRack',
                               'VrLieView', 'VrLyingPose'],
    'Content/Tools/Belt': ['PcToolBeltBar', 'VrToolBelt'],
    # 차량
    'Content/Vehicle/Body': ['VehicleBody', 'VehicleEngine', 'VehicleSync', 'VehiclePartsManager'],
    'Content/Vehicle/Select': ['VehicleSelectionController', 'VehicleInfoUIManager', 'VehicleSpawner', 'VehicleSpinner'],
    'Content/Vehicle/Lift': ['ILiftController', 'LiftControllerBase', 'LiftAnimationEventHandler', 'LiftButtonUI', 'LiftingTool', 'VehicleLiftController',
                             'BatteryLiftController', 'BatteryLiftMover', 'BatteryJack'],
    'Content/Vehicle/Battery': ['Battery', 'BusbarPart', 'MSDPart', 'ModuleData', 'ModulePart', 'SlotManager'],
    'Content/Vehicle/Service': ['AirtightServiceVisual', 'Battery12VServiceVisual', 'CoolantServiceVisual', 'VehicleCoolant'],
    'Content/Vehicle/Diagnostics': ['DTCData', 'DiagnosticSystem', 'GDS_TabletController', 'GdsModel', 'GdsTablet'],
    # 자유 실습(분해·조립)
    'Content/Disassembly/Parts': ['CarPartService', 'CarPartService.Tray', 'CarPartItem', 'InteractablePart', 'DisassemblyManager', 'FreeModePartAttachment', 'FreeModePartData'],
    'Content/Disassembly/Bolt': ['Bolt', 'BoltGroupCounter', 'BoltPartName', 'FreeWorkBolt'],
    'Content/Disassembly/Rules': ['CarPartRules', 'CarPartLube'],
    'Content/Disassembly/Tire': ['TireService', 'TireService.Tray', 'TireWheel'],
    'Content/Disassembly/Tray': ['PartTray', 'PartTrayPanel', 'PartTrayPanel.Tabs', 'VrPartTray'],
    'Content/Disassembly/Ui': ['CarPartListPanel', 'MountedPartPrompt'],
    # 정비 매뉴얼
    'Content/Manual': ['ManualBoard', 'ManualBoardButton', 'ManualTips', 'TorqueSpec'],
    'Content/Manual/Inspection': ['ManualInspection', 'BrakeInspection', 'SuspensionInspection', 'TireInspection'],
    # 월드(씬 고정 오브젝트)
    'Content/World': ['WorldSfx', 'NetZoneActivator', 'SnapZoneTrigger'],
    'Content/World/Interactive': ['ClickableAnimator', 'Openable'],
    # 디버그·잡
    'Content/Debug': ['EVDebugCommands', 'ActiveDebugger', 'Test', 'NewMonoBehaviourScript'],
}
# 외부 코드(손대지 않음): Content/External/**
KEEP_PREFIXES = ['Content/External/']
