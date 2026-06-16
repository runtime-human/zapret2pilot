# Zapret2Pilot / Z2P — UI Design Canon

## 1. UI direction

Zapret2Pilot uses a clean Windows desktop dashboard style:

- light theme by default;
- modern card layout;
- left sidebar navigation;
- clear status hierarchy;
- no duplicated status indicators;
- Russian-first labels.

The UI must feel like a focused runtime manager, not a gaming launcher or hacking tool.

## 2. Sidebar

Sidebar content:

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

────────────────

Документация
О программе
```

Sidebar must not show runtime status in the lower-left corner.

Do not show in sidebar:

- Service: Running;
- Runtime: Active;
- Обход активен;
- Health OK;
- repeated runtime status blocks.

## 3. Top-right area

Top-right contains only:

```text
RU
Settings icon
Minimize
Maximize
Close
```

Do not show status chips in the top bar.

## 4. Dashboard status card

Primary dashboard card:

```text
[Large green check]

Обход активен
Текущий профиль работает стабильно.
Ключевые проверки пройдены.

[Остановить] [Проверить сейчас] [Диагностика]

Right metadata:
  Время работы          2 ч 47 мин
  Режим работы          Автопилот
  Текущий профиль       Сбалансированный
  Последняя проверка    12:43 (2 мин назад)
```

The icon near `Остановить` should follow the approved previous design direction and not introduce a new random icon style.

## 5. Dashboard cards

### Режим работы

```text
Режим работы

Автопилот
Z2P автоматически выбирает профиль,
проверяет сеть и использует резервный
сценарий при проблемах.

Последнее действие:
проверил сервисы — профиль не менялся.

Подробнее о режиме →
```

### Текущий профиль обхода

```text
Текущий профиль обхода

Сбалансированный    Рекомендуется

Оптимальный баланс скорости, стабильности
и совместимости для большинства сетей.

Выбран автоматически на основе
диагностики сети и доступности сервисов.

[Закрепить профиль] [Подробнее →]
```

### Проверка ключевых сервисов

```text
Проверка ключевых сервисов                    [Проверить]

YouTube      Доступен      124 мс
Discord      Доступен      146 мс
Telegram     Доступен       98 мс

Обновлено: 12:43
```

Do not write `Отлично` if status already says `Доступен`. Latency is more useful.

### Диагностика обхода

Because there is no Windows Service, do not say `Служба Z2P`.

Use:

```text
Диагностика обхода

Сеть                    OK
DNS                     OK
Модуль zapret2          OK
Резервный сценарий      Готов
Контроллер обхода       OK

[Открыть полную диагностику →]
```

### Последние события

```text
Последние события

12:43:15  Проверка сервисов завершена — все доступны     Успех
12:42:08  Профиль “Сбалансированный” применён             Инфо
12:41:22  DNS проверка успешна                            Успех
12:40:55  Резервный сценарий проверен — готов             Инфо
12:40:12  Обход запущен                                   Успех

[Перейти ко всем событиям →]
```

## 6. Runtime states

### Stopped

```text
Обход остановлен
Zapret2Pilot готов к запуску.
Выберите профиль или используйте Автопилот.

[Запустить] [Auto Doctor] [Диагностика]
```

### Starting

```text
Обход запускается
Применяется профиль “Сбалансированный”.
Проверяется runtime zapret2.

[Отменить]
```

### Running

```text
Обход активен
Текущий профиль работает стабильно.
Ключевые проверки пройдены.

[Остановить] [Проверить сейчас] [Диагностика]
```

### Degraded

```text
Обход работает нестабильно
Обнаружены проблемы с текущим профилем.
Можно запустить Auto Doctor или откатиться.

[Auto Doctor] [Откатить профиль] [Остановить]
```

### Crashed

```text
Обход остановлен из-за ошибки
Runtime завершился неожиданно.
Сохранён диагностический отчёт.

[Перезапустить] [Открыть диагностику] [Экспорт отчёта]
```

### Not elevated

```text
Нужны права администратора
Zapret2Pilot управляет runtime zapret2
и требует повышенные права.

[Перезапустить от имени администратора]
[Закрыть]
```

## 7. Tray states

The no-duplicate-status rule applies to the main window, not to tray.

Tray icon must reflect runtime state:

- gray: stopped;
- green: running;
- yellow: degraded;
- red: crashed/blocked;
- spinner/blue overlay: starting/checking.

## 8. Design tokens

Do not use random inline colors.

Base tokens:

```xml
<SolidColorBrush x:Key="Z2P.Color.Primary" Color="#2563EB" />
<SolidColorBrush x:Key="Z2P.Color.Success" Color="#16A34A" />
<SolidColorBrush x:Key="Z2P.Color.Warning" Color="#F59E0B" />
<SolidColorBrush x:Key="Z2P.Color.Danger" Color="#DC2626" />
<SolidColorBrush x:Key="Z2P.Color.TextPrimary" Color="#0F172A" />
<SolidColorBrush x:Key="Z2P.Color.TextSecondary" Color="#475569" />

<x:Double x:Key="Z2P.Radius.Card">16</x:Double>
<x:Double x:Key="Z2P.Spacing.4">16</x:Double>
<x:Double x:Key="Z2P.Spacing.6">24</x:Double>
```

## 9. Icon rules

- one stroke style;
- consistent line thickness;
- navigation icons outline;
- status icons may be filled/strong;
- do not mix random icon packs.

## 10. Performance rules

- compiled bindings where possible;
- lazy screen loading;
- virtualized logs/events;
- no blocking I/O on UI thread;
- dashboard snapshots throttled;
- no direct database reads from ViewModel;
- no runtime process logic inside ViewModel.

## 11. Copywriting rules

- short Russian labels;
- no marketing exaggeration;
- no scary hacker vocabulary;
- do not call Z2P VPN;
- mention downtime when applying profile;
- use clear recovery actions after errors.