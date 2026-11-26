using Zen.Ui;
namespace Zen.Rpg;

public sealed class MainForm : ZenForm
{
    private Journey _journey = new(new Guardian());
    private readonly Label _stats = Caption(""), _location = Caption(""), _enemy = Caption("");
    private readonly ListBox _inventory = new() { Width = 410, Height = 170, BorderStyle = BorderStyle.None, BackColor = Stone };
    private readonly RichTextBox _log = new() { Width = 530, Height = 170, ReadOnly = true, BackColor = Paper, BorderStyle = BorderStyle.None };
    private readonly FlowLayoutPanel _paths = Row();
    private readonly Button _ability;
    public MainForm() : base("Путь сквозь туман", "05 / ПОШАГОВАЯ RPG     ·     Не спешить. Продолжать путь.")
    {
        var classes = Choice(["Страж · сила", "Мудрец · магия", "Странник · лечение"], 240);
        Body.Controls.Add(Row(classes, Button("Новое путешествие", () => { _journey = new(HeroFactory.Create(new[] { "guardian", "sage", "wanderer" }[classes.SelectedIndex])); Bind(); _log.Clear(); RefreshGame(); }, true), Button("Сохранить", () => FileAction(true)), Button("Загрузить", () => FileAction(false))));
        Body.Controls.Add(_stats);
        Body.Controls.Add(_location);
        Body.Controls.Add(_enemy);
        Body.Controls.Add(_paths);
        _ability = Button("Способность", () => Run(_journey.Ability));
        Body.Controls.Add(Row(Button("Начать бой", () => Run(_journey.Fight)), Button("Атака", () => Run(_journey.Attack), true), _ability, Button("Отступить", () => Run(_journey.Escape)), Button("Искать находки", () => Run(_journey.Collect))));
        Body.Controls.Add(Row(new Label { Text = "ИНВЕНТАРЬ", Width = 410, Height = 20 }, new Label { Text = "ЖУРНАЛ ПУТЕШЕСТВИЯ", Width = 530, Height = 20 }));
        Body.Controls.Add(Row(_inventory, _log));
        Body.Controls.Add(Row(Button("Использовать", () => Run(() => _journey.Use(Selected()))), Button("Надеть", () => Run(() => _journey.Equip(Selected()))), Button("Снять", () => Run(() => _journey.Unequip(Selected())))));
        var shop = Choice(["Настой · 10", "Клинок · 25", "Доспех · 20"], 200);
        Body.Controls.Add(Row(shop, Button("Купить у торговца", () => Run(() => _journey.Buy(new[] { "potion", "weapon", "armor" }[shop.SelectedIndex])))));
        Bind();
        RefreshGame();
    }
    private Item Selected() => _inventory.SelectedItem as Item ?? throw new ArgumentException("Выберите предмет.");
    private void Bind() => _journey.Happened += message => { _log.AppendText(message + "\n\n"); _log.SelectionStart = _log.TextLength; _log.ScrollToCaret(); };
    private void Run(Action action)
    {
        action();
        RefreshGame();
    }
    private void RefreshGame()
    {
        var hero = _journey.Hero;
        _stats.Text = $"{hero.Name}  /  УРОВЕНЬ {hero.Level}     ·     ЗДОРОВЬЕ {hero.Health}/{hero.MaxHealth}     ·     МАНА {hero.Mana}/{hero.MaxMana}\nСИЛА {hero.Strength}     ЗАЩИТА {hero.Defense}     ОПЫТ {hero.Experience}/{hero.Level * 20}     ЗОЛОТО {hero.Gold}";
        _location.Text = _journey.Current.Name + " — " + _journey.Current.Description;
        _enemy.Text = _journey.State switch
        {
            JourneyState.Victory => "ПОБЕДА · Туман рассеялся. Путь завершён.",
            JourneyState.Defeat => "ПОРАЖЕНИЕ · Каждое путешествие оставляет опыт. Начните заново.",
            _ => $"{(_journey.State == JourneyState.Combat ? "БОЙ" : "ЛОКАЦИЯ")} · {_journey.Current.Enemy.Name} · {_journey.Current.Enemy.Health}/{_journey.Current.Enemy.MaxHealth} HP"
        };
        _enemy.ForeColor = _journey.State == JourneyState.Defeat ? Red : Matcha;
        _ability.Text = hero.AbilityName;
        var selection = _inventory.SelectedItem;
        _inventory.Items.Clear();
        _inventory.Items.AddRange(hero.Inventory.ToArray());
        if (selection != null)
            _inventory.SelectedItem = selection;
        _paths.Controls.Clear();
        foreach (var index in _journey.Current.Exits)
        {
            var destination = index;
            _paths.Controls.Add(Button("→ " + _journey.Locations[index].Name, () => Run(() => _journey.Move(destination))));
        }
    }
    private void FileAction(bool save)
    {
        using FileDialog dialog = save ? new SaveFileDialog() : new OpenFileDialog();
        dialog.Filter = "Путешествие JSON|*.json";
        if (dialog.ShowDialog() != DialogResult.OK)
            return;
        if (save)
            _journey.Save(dialog.FileName);
        else
        {
            _journey = Journey.Load(dialog.FileName);
            Bind();
            _log.Clear();
            RefreshGame();
        }
    }
}
