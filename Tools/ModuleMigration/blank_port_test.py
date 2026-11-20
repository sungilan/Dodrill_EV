#!/usr/bin/env python3
"""
빈 프로젝트 이식 시험 — 새 Unity 프로젝트를 만들어 플러그인과 모듈 폴더만 복사한 뒤 컴파일 오류를 센다.
  python blank_port_test.py <대상 폴더> [--modules Common,Interaction,Avatar,Engine,Ui,Safety,Solo] [--odin] [--skip-create] [--runtime]
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
    for m in mods: copy(os.path.join(A, '01_Script', m), os.path.join(T, '01_Script', m))
    copy(os.path.join(A, '99_ModuleExample'), os.path.join(T, '99_ModuleExample'))
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
        shutil.copy2(os.path.join(HERE, 'PortCheck.cs.txt'), os.path.join(T, 'Editor', 'PortCheck.cs'))
        rlog = os.path.join(target, 'port_runtime.log')
        r = run([UNITY, '-batchmode', '-projectPath', target, '-executeMethod', 'PortCheck.Run', '-logFile', rlog])
        print('런타임 시험 종료 코드 %d' % r.returncode)
        for line in open(rlog, encoding='utf-8', errors='replace'):
            if '[PortCheck]' in line or re.search(r'error CS\d+|Exception', line): print(line.rstrip()[:260])


if __name__ == '__main__':
    main()
