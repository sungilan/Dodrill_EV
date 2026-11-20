#!/usr/bin/env python3
"""
빈 프로젝트 이식 시험 — 새 Unity 프로젝트를 만들어 플러그인과 모듈 폴더만 복사한 뒤 컴파일 오류를 센다.
  python blank_port_test.py <대상 폴더> [--modules Common,Interaction,Avatar,Engine,Ui,Safety,Solo] [--odin] [--skip-create] [--runtime] [--lobby] [--roomstart] [--package <파일.unitypackage>] [--lean]
  · --lobby : Lobby 모듈(+MST)도 복사하고, --runtime 이면 PortCheck 대신 LobbyCheck(로비 흐름 시험)를 실행
  · --lean : 서버 빌드 시험용 — Avatar 의 무거운 아트·프리팹(Avatar/Art, Avatar/Prefabs)을 시험 프로젝트에서 뺀다(스크립트는 유지). 윈도우 룸 서버 빌드의 셰이더 변형 폭증을 피함
  · --package <파일> : 모듈 폴더를 복사하는 대신 이 .unitypackage 를 -importPackage 로 가져온 뒤 컴파일(패키지 검증용; 플러그인·태그·DOTween 설정은 그대로 복사)
  · --roomstart : (--lobby 와 함께) 룸 서버·마스터 서버를 윈도우 플레이어로 빌드하고, 마스터를 띄운 뒤 에디터 클라이언트로 방 만들기→준비→시작→룸 서버 기동까지 확인(수 분 걸림)
  · --runtime : 컴파일이 통과하면 PortCheck.cs 를 넣고 예제 씬을 만들어 Play 로 시나리오 완주까지 확인(로그 [PortCheck])
  · 대상 폴더는 저장소 밖에 둔다(Library 가 크다). 이미 있으면 Assets/Packages 를 비우고 다시 복사한다.
  · --odin : Odin(Sirenix)도 복사(기본은 복사하지 않음 → Common/Core/OdinShim.cs 시험)
  · 로그: <대상>/port_compile.log, 오류 요약: <대상>/port_errors.txt
복사하는 것
  플러그인  Assets/Plugins/{Demigiant,RootMotion,SerializedCollections,Easy performant outline}, Assets/Packages/{FishNet,AutoHand,MasterServerToolkit,TextMesh Pro}
  모듈      Assets/01_Script/<모듈>  (+ 99_ModuleExample)
  설정      ProjectSettings/TagManager.asset(태그·레이어), Assets/Resources/DOTweenSettings.asset(DOTween asmdef 생성 설정)
  패키지    Packages/manifest.json 에 저장소와 같은 버전의 XR·URP·Localization 등을 더한다
"""
import json, os, shutil, subprocess, sys, re, time
HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, '..', '..'))
UNITY = r'C:\Program Files\Unity\Hub\Editor\6000.3.10f1\Editor\Unity.exe'

PLUGINS = ['Plugins/Demigiant', 'Plugins/RootMotion', 'Plugins/SerializedCollections', 'Plugins/Easy performant outline',
           'Packages/FishNet', 'Packages/AutoHand', 'Packages/MasterServerToolkit', 'Packages/TextMesh Pro']
WANT_PKGS = ['com.unity.localization', 'com.unity.render-pipelines.core', 'com.unity.render-pipelines.universal',
             'com.unity.render-pipelines.universal-config', 'com.unity.shadergraph', 'com.unity.xr.arfoundation', 'com.unity.xr.hands',
             'com.unity.xr.interaction.toolkit', 'com.unity.xr.management', 'com.unity.xr.meta-openxr', 'com.unity.xr.openxr',
             'com.unity.burst', 'com.unity.collections', 'com.unity.mathematics']


def run(cmd, **kw):
    print('>', ' '.join(cmd)); sys.stdout.flush()
    return subprocess.run(cmd, **kw)


def copy(src, dst):
    if os.path.isdir(src):
        shutil.copytree(src, dst, dirs_exist_ok=True)
        if os.path.exists(src + '.meta'): shutil.copy2(src + '.meta', dst + '.meta')
    else:
        shutil.copy2(src, dst)
        if os.path.exists(src + '.meta'): shutil.copy2(src + '.meta', dst + '.meta')


def main():
    args = sys.argv[1:]
    if not args: sys.exit(__doc__)
    target = os.path.abspath(args[0])
    mods = 'Common,Interaction,Avatar,Engine,Ui,Safety,Solo'
    if '--modules' in args: mods = args[args.index('--modules') + 1]
    mods = [m for m in mods.split(',') if m]
    lobby = '--lobby' in args
    lean = '--lean' in args
    pkg = os.path.abspath(args[args.index('--package') + 1]) if '--package' in args else None
    if lobby and 'Lobby' not in mods: mods.append('Lobby')
    odin = '--odin' in args
    A = os.path.join(REPO, 'Assets')
    T = os.path.join(target, 'Assets')

    if '--skip-create' not in args and not os.path.exists(os.path.join(target, 'ProjectSettings')):
        os.makedirs(os.path.dirname(target), exist_ok=True)
        r = run([UNITY, '-batchmode', '-quit', '-createProject', target, '-logFile', os.path.join(os.path.dirname(target), 'port_create.log')])
        if r.returncode: sys.exit('프로젝트 생성 실패 (%d)' % r.returncode)

    for sub in ('Plugins', 'Packages', '01_Script', '99_ModuleExample'):
        p = os.path.join(T, sub)
        if os.path.exists(p): shutil.rmtree(p)
        if os.path.exists(p + '.meta'): os.remove(p + '.meta')
    os.makedirs(os.path.join(T, 'Plugins'), exist_ok=True); os.makedirs(os.path.join(T, 'Packages'), exist_ok=True)
    os.makedirs(os.path.join(T, '01_Script'), exist_ok=True); os.makedirs(os.path.join(T, 'Resources'), exist_ok=True)
    for p in PLUGINS: copy(os.path.join(A, p), os.path.join(T, p))
    if odin: copy(os.path.join(A, 'Plugins/Sirenix'), os.path.join(T, 'Plugins/Sirenix'))
    if not pkg:
        for m in mods: copy(os.path.join(A, '01_Script', m), os.path.join(T, '01_Script', m))
        if 'Avatar' in mods and not lean: copy(os.path.join(A, '99_ModuleExample'), os.path.join(T, '99_ModuleExample'))   # 예제 에디터 어셈블리가 Avatar 를 참조
    if lean and not pkg:
        for sub in ('Art', 'Prefabs'):
            d = os.path.join(T, '01_Script', 'Avatar', sub)
            if os.path.isdir(d): shutil.rmtree(d)
            if os.path.exists(d + '.meta'): os.remove(d + '.meta')
        print('lean: Avatar/Art, Avatar/Prefabs 제거')
    shutil.copy2(os.path.join(REPO, 'ProjectSettings', 'TagManager.asset'), os.path.join(target, 'ProjectSettings', 'TagManager.asset'))
    for f in ('DOTweenSettings.asset',):
        s = os.path.join(A, 'Resources', f)
        if os.path.exists(s): copy(s, os.path.join(T, 'Resources', f))

    # 패키지: 새 프로젝트 기본 manifest 에 저장소 버전을 더한다
    mp = os.path.join(target, 'Packages', 'manifest.json')
    man = json.load(open(mp, encoding='utf-8'))
    src = json.load(open(os.path.join(REPO, 'Packages', 'manifest.json'), encoding='utf-8'))['dependencies']
    for k in WANT_PKGS:
        if k in src: man['dependencies'][k] = src[k]
    json.dump(man, open(mp, 'w', encoding='utf-8'), indent=2)

    if pkg:
        ilog = os.path.join(target, 'port_import.log')
        t1 = time.time()
        r = run([UNITY, '-batchmode', '-quit', '-projectPath', target, '-importPackage', pkg, '-logFile', ilog])
        print('패키지 가져오기 종료 코드 %d, %.0f초' % (r.returncode, time.time() - t1))
    log = os.path.join(target, 'port_compile.log')
    t0 = time.time()
    r = run([UNITY, '-batchmode', '-quit', '-projectPath', target, '-logFile', log])
    print('Unity 종료 코드 %d, %.0f초' % (r.returncode, time.time() - t0))
    errs = []
    for line in open(log, encoding='utf-8', errors='replace'):
        if re.search(r'error CS\d+|error\s*:|Scripts have compiler errors|Compilation failed', line):
            errs.append(line.rstrip())
    seen = []
    for e in errs:
        if e not in seen: seen.append(e)
    open(os.path.join(target, 'port_errors.txt'), 'w', encoding='utf-8').write('\n'.join(seen))
    print('컴파일 오류 %d줄(중복 제거) → port_errors.txt' % len(seen))
    for e in seen[:40]: print(e[:220])
    if '--runtime' in args and not any('error CS' in e for e in seen):
        os.makedirs(os.path.join(T, 'Editor'), exist_ok=True)
        check, method = ('LobbyCheck', 'LobbyCheck.Run') if lobby else ('PortCheck', 'PortCheck.Run')
        shutil.copy2(os.path.join(HERE, check + '.cs.txt'), os.path.join(T, 'Editor', check + '.cs'))
        if lobby:   # 로비 시험용 시나리오 JSON(Resources/Scenarios)
            os.makedirs(os.path.join(T, 'Resources', 'Scenarios'), exist_ok=True)
            shutil.copy2(os.path.join(A, '99_ModuleExample', 'Scenarios', 'example_workbench.json'), os.path.join(T, 'Resources', 'Scenarios', 'example_workbench.json'))
        rlog = os.path.join(target, 'port_runtime.log')
        r = run([UNITY, '-batchmode', '-projectPath', target, '-executeMethod', method, '-logFile', rlog])
        print('런타임 시험 종료 코드 %d' % r.returncode)
        for line in open(rlog, encoding='utf-8', errors='replace'):
            if '[PortCheck]' in line or '[LobbyCheck]' in line or re.search(r'error CS\d+|Exception', line): print(line.rstrip()[:260])

    if '--roomstart' in args and lobby:
        roomstart(target, T, A)


def kill_all():
    for n in ('MasterServer.exe', 'RoomServer.exe'):
        subprocess.run(['taskkill', '/F', '/IM', n], capture_output=True)


def roomstart(target, T, A):
    outdir = os.path.join(target, 'ServerBuilds')
    shutil.rmtree(outdir, ignore_errors=True)
    os.makedirs(outdir, exist_ok=True)
    os.makedirs(os.path.join(T, 'Editor'), exist_ok=True)
    shutil.copy2(os.path.join(HERE, 'RoomBuild.cs.txt'), os.path.join(T, 'Editor', 'RoomBuild.cs'))
    shutil.copy2(os.path.join(HERE, 'LobbyCheck.cs.txt'), os.path.join(T, 'Editor', 'LobbyCheck.cs'))
    os.makedirs(os.path.join(T, 'Resources', 'Scenarios'), exist_ok=True)
    shutil.copy2(os.path.join(A, '99_ModuleExample', 'Scenarios', 'example_workbench.json'), os.path.join(T, 'Resources', 'Scenarios', 'example_workbench.json'))
    env = dict(os.environ, LOBBY_BUILD_DIR=outdir)
    blog = os.path.join(target, 'port_build.log')
    print('서버 빌드 중(수 분)…'); sys.stdout.flush()
    r = subprocess.run([UNITY, '-batchmode', '-projectPath', target, '-executeMethod', 'RoomBuild.BuildAll', '-logFile', blog], env=env)
    for line in open(blog, encoding='utf-8', errors='replace'):
        if '[RoomBuild]' in line or 'error CS' in line: print(line.rstrip()[:240])
    master = os.path.join(outdir, 'Master', 'MasterServer.exe')
    room = os.path.join(outdir, 'Room', 'RoomServer.exe')
    if not (os.path.exists(master) and os.path.exists(room)):
        print('빌드 실패 — 중단'); return
    kill_all()
    mlog = os.path.join(target, 'port_master.log')
    mproc = subprocess.Popen([master, '-mstStartMaster', '-mstMasterIp', '127.0.0.1', '-mstMasterPort', '9035', '-logFile', mlog])
    print('MasterServer.exe 실행 pid', mproc.pid); sys.stdout.flush()
    ok = False
    for _ in range(60):
        time.sleep(1)
        if os.path.exists(mlog) and 'listening' in open(mlog, encoding='utf-8', errors='replace').read():
            ok = True; break
    print('마스터 listening 확인:', ok)
    if ok:
        rlog = os.path.join(target, 'port_roomstart.log')
        env2 = dict(os.environ, LOBBYCHECK_ROOMSTART='1')
        r2 = subprocess.run([UNITY, '-batchmode', '-projectPath', target, '-executeMethod', 'LobbyCheck.Run', '-logFile', rlog], env=env2)
        print('클라이언트 시험 종료 코드', r2.returncode)
        for line in open(rlog, encoding='utf-8', errors='replace'):
            if '[LobbyCheck]' in line and ('정보로그' not in line): print(line.rstrip()[:260])
    time.sleep(2)
    kill_all()
    try: mproc.wait(timeout=5)
    except Exception: pass
    print('--- 마스터 로그(주요) ---')
    if os.path.exists(mlog):
        for line in open(mlog, encoding='utf-8', errors='replace'):
            if any(k in line for k in ('Spawn', 'spawn', 'Room', 'room', 'listening', 'Lobby', 'Starting process', 'Error', 'Exception')): print(line.rstrip()[:240])


if __name__ == '__main__':
    main()
