# Engine 프리팹 — 필요 에셋 목록

> 자동 생성(2026-11-20, 프리팹 모듈화). 이 모듈 폴더(`Assets/01_Script/Engine`)를 다른 프로젝트로 옮길 때, 아래 '폴더 밖 에셋'도 함께 가져가야 프리팹이 깨지지 않는다.
> 플러그인·패키지(Packages/, Plugins/)는 목록에서 뺐다 — 해당 플러그인을 새 프로젝트에 설치하면 된다.

## 이 모듈 폴더 안의 프리팹 (3개)
- `Prefabs/NetworkManagerHost.prefab`
- `Prefabs/ScenarioEngine.prefab`
- `Prefabs/SpawnPoint.prefab`

## 폴더 밖 에셋 (5개)

### `Assets/01_Script/Avatar/Art/` (2개)
- `Materials/Generic_PlayerBody.mat`  ← NetworkManagerHost
- `Materials/Generic_PlayerFace.mat`  ← NetworkManagerHost

### `Assets/01_Script/Avatar/Prefabs/` (1개)
- `Players/GenericPlayer.prefab`  ← NetworkManagerHost

### `Assets/01_Script/Interaction/Art/` (1개)
- `Materials/Generic_Item.mat`  ← NetworkManagerHost

### `Assets/01_Script/Interaction/Prefabs/` (1개)
- `NetworkItem.prefab`  ← NetworkManagerHost
