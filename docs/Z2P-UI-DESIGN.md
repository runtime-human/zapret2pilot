# Zapret2Pilot / Z2P — UI Design Canon

## 1. General direction

UI style:

- light theme;
- clean desktop dashboard;
- rounded cards;
- restrained blue/green accents;
- Russian-first copy;
- no visual noise;
- no duplicate status indicators.

## 2. Shell layout

Sidebar:

```text
Zapret2Pilot
v0.0.1

Главная
Профили
Правила
Auto Doctor
Диагностика
Логи
Модуль zapret2
Настройки

---

Документация
О программе
```

Do not show runtime status in lower-left sidebar.

Top-right:

```text
RU
Settings icon
Minimize
Maximize
Close
```

Do not show runtime status chips in top header.

## 3. Dashboard status card

Main status is shown only in the large dashboard card.

Running state:

```text
[large green check]

Обход активен
Текущий профиль работает стабильно.
Ключевые проверки пройдены.

[Остановить] [Проверить сейчас] [Диагностика]

Время работы          2 ч 47 мин
Режим работы          Автопилот
Текущий профиль       Сбалансированный
Последняя проверка    12:43 (2 мин назад)
```

Stopped state:

```text
Обход остановлен
Zapret2Pilot готов к запуску.
Выберите профиль или используйте Автопилот.

[Запустить] [Auto Doctor] [Диагностика]
```

Degraded state:

```text
Обход работает нестабильно
Обнаружены проблемы с текущим профилем.
Можно запустить Auto Doctor или откатиться.

[Auto Doctor] [Откатить профиль] [Остановить]
```

Crashed state:

```text
Обход остановлен из-за ошибки
Runtime завершился неожиданно.
Сохранён диагностический отчёт.

[Перезапустить] [Открыть диагностику] [Экспорт отчёта]
```

## 4. Dashboard cards

### Режим работы

```text
Автопилот
Z2P автоматически выбирает профиль, проверяет сеть и использует резервный сценарий при проблемах.

Последнее действие: проверил сервисы — профиль не менялся.
```

### Текущий профиль обхода

```text
Сбалансированный    Рекомендуется

Оптимальный баланс скорости, стабильности и совместимости для большинства сетей.

[Закрепить профиль] [Подробнее]
```

### Проверка ключевых сервисов

```text
YouTube      Доступен      124 мс
Discord      Доступен      146 мс
Telegram     Доступен       98 мс

Обновлено: 12:43
```

Do not write `Отлично` when `Доступен` and latency are already shown.

### Диагностика обхода

```text
Сеть                    OK
DNS                     OK
Модуль zapret2          OK
Резервный сценарий      Готов
Контроллер обхода       OK
```

Do not write `Служба Z2P`, because the architecture has no Windows Service.

### Последние события

Show latest 5 events only. Full logs screen must be virtualized.

## 5. Tray

Tray is allowed to duplicate runtime state because it is outside the main window.

Tray states:

- gray: stopped;
- green: running;
- yellow: degraded;
- red: crashed/blocked;
- blue/spinner: starting/checking.

## 6. Design-system rules

- Use design tokens for colors, spacing and radius.
- Use one icon stroke style.
- Navigation icons are outline.
- Status icons may be filled.
- Do not use random inline colors.
- Use compiled bindings where possible.
- Use lazy screen loading.
- Use virtualized lists for logs/events/hostlists.

## 7. Copy rules

Do not call Z2P a VPN.

Use direct Russian UI wording:

- `Обход активен`;
- `Обход остановлен`;
- `Проверить сейчас`;
- `Диагностика`;
- `Закрепить профиль`;
- `Экспорт отчёта`.

Warnings must be precise and short.
