// ============================================================
//  ToolMarkers.cs — 장비 종류를 직접 알지 않고 '이런 물건' 으로만 구분하기 위한 표시 인터페이스 (11/20)
//  · IHoldScaleExempt : PC·모바일이 들 때 '화면의 40% 이하'로 줄이는 홀드 스케일을 적용하지 않을 물건(HoldScalePolicy)
//  · IFloorCarried    : 바닥을 끌거나 들고 다니는 큰 운반 장비(바스켓·오일 받이 등) — 서랍 안 물건 검사에서 제외(Openable)
// ============================================================
public interface IHoldScaleExempt { }

public interface IFloorCarried { }
