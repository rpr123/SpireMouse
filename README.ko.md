# Spire Mouse

[English](README.md) · [Steam 창작마당](https://steamcommunity.com/sharedfiles/filedetails/?id=3808397695&l=koreana)

**Slay the Spire 2**에서 우클릭으로 카드를 사용하고, 배속을 조절하고, 마우스 사이드 버튼으로 Undo·Redo하거나 층을 재시작할 수 있습니다.

![Spire Mouse 조작법](workshop/image.png)

## 설치와 조작

창작마당에서 구독한 뒤 게임의 모드 메뉴에서 Spire Mouse를 활성화하세요. 필수 선행 모드는 없습니다. Windows의 게임 v0.113.1 public-beta에서 동작을 확인했습니다. [Steam 창작마당](https://steamcommunity.com/sharedfiles/filedetails/?id=3808397695&l=koreana)

| 기본 입력 | 동작 |
|---|---|
| 카드 우클릭 | 카드 사용. 대상이 필요하면 대상도 우클릭하거나, 우클릭으로 대상까지 드래그한 뒤 놓기 |
| 카드 선택창에서 우클릭 | 선택·해제. 선택 조건을 충족한 뒤 빈 곳을 우클릭해 확정 |
| 전투의 빈 곳 더블 우클릭 | 턴 종료 |
| 보상·선택을 마친 뒤 빈 곳 우클릭 | 다음으로 진행 |
| 뒤쪽 사이드 버튼(M4) | Undo |
| 앞쪽 사이드 버튼(M5) | Redo |
| 두 사이드 버튼 함께 누르기 / F5 | 층 재시작 |
| Esc | 대상 지정 취소 |

**게임 설정 → Spire Mouse**에서 단축키, 배속, 기록 옵션을 조정할 수 있습니다.

## 전투 재시작도 되돌리기

전투 재시작 후 **Undo**를 누르면 진행 중이던 카드 선택까지 재시작 직전으로 돌아갑니다. **Redo**를 누르면 재시작 직후로 돌아갑니다. Undo한 뒤 새 행동을 하면 기존 Redo 기록은 사라집니다.

Undo·Redo와 재시작은 싱글플레이 전용입니다. Undo 기록은 현재 전투에만 적용되며 전투를 끝내거나 게임을 종료하면 사용할 수 없습니다. 비전투 방의 재시작이나 이벤트 선택은 되돌릴 수 없습니다.

## 버그 제보

게임과 모드 버전, 문제를 재현하는 순서를 남겨 주세요. [GitHub Issues](https://github.com/rpr123/SpireMouse/issues)

## 라이선스

Spire Mouse는 MIT 라이선스를 사용합니다. 배속 구성 요소는 OLC의 STS2-QuickAnimationMode를 바탕으로 수정했습니다. [MIT 라이선스](LICENSE) · [배속 구성 요소 라이선스](LICENSE-SpeedControl.txt) · [개발 문서](docs/BUILD.md)
