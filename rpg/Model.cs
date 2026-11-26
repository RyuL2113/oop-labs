namespace Zen.Rpg;

public abstract class Character
{
    public string Name
    {
        get;
    }
    public int Health
    {
        get; private set;
    }
    public int MaxHealth
    {
        get; protected set;
    }
    public abstract int Strength
    {
        get;
    }
    public virtual int Defense => 0;
    public bool Alive => Health > 0;
    protected Character(string name, int health)
    {
        Name = name;
        MaxHealth = Health = health;
    }
    public void TakeDamage(int amount)
    {
        if (amount < 0)
            throw new ArgumentException("Отрицательный урон.");
        Health = Math.Max(0, Health - Math.Max(1, amount - Defense));
    }
    public void Heal(int amount)
    {
        if (amount < 0)
            throw new ArgumentException("Отрицательное лечение.");
        Health = Math.Min(MaxHealth, Health + amount);
    }
    internal void RestoreHealth(int value)
    {
        if (value < 0 || value > MaxHealth)
            throw new InvalidDataException("Некорректное здоровье.");
        Health = value;
    }
}
public abstract class Item(string id, string name, int price)
{
    public string Id { get; } = id;
    public string Name { get; } = name;
    public int Price { get; } = price;
    public virtual int AttackBonus => 0;
    public virtual int DefenseBonus => 0;
    public virtual string? Slot => null;
    public abstract void Use(Hero hero);
    public virtual void Equip(Hero hero) => throw new InvalidOperationException("Этот предмет нельзя надеть.");
    public override string ToString() => Name;
}
public sealed class Weapon() : Item("weapon", "Клинок путника · +5 силы", 25)
{
    public override int AttackBonus => 5;
    public override string Slot => "weapon";
    public override void Equip(Hero hero) => hero.Equip(this);
    public override void Use(Hero hero) => Equip(hero);
}
public sealed class Armor() : Item("armor", "Льняной доспех · +3 защиты", 20)
{
    public override int DefenseBonus => 3;
    public override string Slot => "armor";
    public override void Equip(Hero hero) => hero.Equip(this);
    public override void Use(Hero hero) => Equip(hero);
}
public sealed class Potion() : Item("potion", "Травяной настой · +35 здоровья", 10)
{
    public override void Use(Hero hero)
    {
        if (hero.Health == hero.MaxHealth)
            throw new InvalidOperationException("Здоровье уже полное.");
        hero.Heal(35);
        hero.Remove(this);
    }
}
public static class ItemFactory
{
    public static Item Create(string id) => id switch { "weapon" => new Weapon(), "armor" => new Armor(), "potion" => new Potion(), _ => throw new InvalidDataException("Неизвестный предмет.") };
}
public abstract class Hero : Character
{
    private readonly List<Item> _inventory = [];
    private readonly Dictionary<string, Item> _equipment = [];
    protected abstract int BaseStrength
    {
        get;
    }
    public abstract string ClassId
    {
        get;
    }
    public abstract string AbilityName
    {
        get;
    }
    public int Level { get; private set; } = 1;
    public int Experience
    {
        get; private set;
    }
    public int Gold { get; private set; } = 20;
    public int Mana { get; private set; } = 30;
    public int MaxMana => 30 + (Level - 1) * 5;
    public override int Strength => BaseStrength + (Level - 1) * 3 + _equipment.Values.Sum(i => i.AttackBonus);
    public override int Defense => _equipment.Values.Sum(i => i.DefenseBonus);
    public IReadOnlyList<Item> Inventory => _inventory.AsReadOnly();
    public IReadOnlyDictionary<string, Item> Equipment => new System.Collections.ObjectModel.ReadOnlyDictionary<string, Item>(_equipment);
    public event Action<int>? LevelUp;
    protected Hero(string name, int health) : base(name, health) { _inventory.Add(new Potion()); }
    public void GainExp(int amount)
    {
        if (amount < 0)
            throw new ArgumentException("Отрицательный опыт.");
        Experience += amount;
        while (Experience >= Level * 20)
        {
            Experience -= Level * 20;
            Level++;
            MaxHealth += 12;
            Heal(MaxHealth);
            Mana = MaxMana;
            LevelUp?.Invoke(Level);
        }
    }
    public void Reward(int gold, int exp)
    {
        if (gold < 0)
            throw new ArgumentException("Отрицательное золото.");
        Gold += gold;
        GainExp(exp);
    }
    public void Buy(Item item)
    {
        if (Gold < item.Price)
            throw new InvalidOperationException("Недостаточно золота.");
        Gold -= item.Price;
        _inventory.Add(item);
    }
    public void Add(Item item) => _inventory.Add(item);
    public void Remove(Item item)
    {
        if (!_inventory.Remove(item))
            throw new InvalidOperationException("Предмет не в инвентаре.");
        foreach (var key in _equipment.Where(x => x.Value == item).Select(x => x.Key).ToArray())
            _equipment.Remove(key);
    }
    public void Equip(Item item)
    {
        if (!_inventory.Contains(item) || item.Slot == null)
            throw new InvalidOperationException("Предмет нельзя надеть.");
        _equipment[item.Slot] = item;
    }
    public void Unequip(Item item)
    {
        var key = _equipment.FirstOrDefault(x => x.Value == item).Key;
        if (key == null)
            throw new InvalidOperationException("Предмет не надет.");
        _equipment.Remove(key);
    }
    protected void SpendMana(int amount)
    {
        if (Mana < amount)
            throw new InvalidOperationException("Не хватает маны.");
        Mana -= amount;
    }
    public abstract string Ability(Enemy enemy);
    internal void Restore(HeroData data)
    {
        if (data.Items is null || data.Equipment is null)
            throw new InvalidDataException("Нет инвентаря или экипировки.");
        if (data.Level is < 1 or > 100 || data.Exp < 0 || data.Exp >= data.Level * 20 || data.Gold < 0)
            throw new InvalidDataException("Некорректные характеристики героя.");
        MaxHealth += (data.Level - 1) * 12;
        Level = data.Level;
        Experience = data.Exp;
        Gold = data.Gold;
        if (data.Mana < 0 || data.Mana > MaxMana)
            throw new InvalidDataException("Некорректная мана.");
        Mana = data.Mana;
        RestoreHealth(data.Health);
        _inventory.Clear();
        _equipment.Clear();
        foreach (var id in data.Items)
            _inventory.Add(ItemFactory.Create(id));
        foreach (var index in data.Equipment)
        {
            if (index < 0 || index >= _inventory.Count)
                throw new InvalidDataException("Некорректная экипировка.");
            Equip(_inventory[index]);
        }
    }
}
public sealed class Guardian() : Hero("Страж", 110)
{
    public override string ClassId => "guardian"; protected override int BaseStrength => 13; public override string AbilityName => "Удар камня · 8 маны";
    public override string Ability(Enemy enemy)
    {
        SpendMana(8);
        enemy.TakeDamage(Strength * 2);
        return "Удар камня";
    }
}
public sealed class Sage() : Hero("Мудрец", 85)
{
    public override string ClassId => "sage"; protected override int BaseStrength => 11; public override string AbilityName => "Огонь фонаря · 10 маны";
    public override string Ability(Enemy enemy)
    {
        SpendMana(10);
        enemy.TakeDamage(Strength * 3);
        return "Огонь фонаря";
    }
}
public sealed class Wanderer() : Hero("Странник", 95)
{
    public override string ClassId => "wanderer"; protected override int BaseStrength => 12; public override string AbilityName => "Дыхание леса · 8 маны";
    public override string Ability(Enemy enemy)
    {
        SpendMana(8);
        Heal(18);
        enemy.TakeDamage(Strength + 8);
        return "Дыхание леса";
    }
}
public static class HeroFactory
{
    public static Hero Create(string id) => id switch { "guardian" => new Guardian(), "sage" => new Sage(), "wanderer" => new Wanderer(), _ => throw new InvalidDataException("Неизвестный класс героя.") };
}
public interface IBehavior
{
    string Act(Enemy self, Hero hero);
}
public sealed class Aggressive : IBehavior
{
    public string Act(Enemy self, Hero hero)
    {
        hero.TakeDamage(self.Strength);
        return self.Name + " атакует.";
    }
}
public sealed class Restoring : IBehavior
{
    public string Act(Enemy self, Hero hero)
    {
        if (self.Health < self.MaxHealth / 2)
        {
            self.Heal(7);
            return self.Name + " восстанавливает здоровье.";
        }
        hero.TakeDamage(self.Strength);
        return self.Name + " наносит удар.";
    }
}
public sealed class Relentless : IBehavior
{
    public string Act(Enemy self, Hero hero)
    {
        hero.TakeDamage(Math.Max(1, self.Strength - 3));
        hero.TakeDamage(Math.Max(1, self.Strength - 3));
        return self.Name + " атакует дважды.";
    }
}
public sealed class Enemy(string name, int health, int strength, IBehavior behavior) : Character(name, health)
{
    public override int Strength => strength;
    public IBehavior Behavior { get; } = behavior;
    public string TakeTurn(Hero hero) => Behavior.Act(this, hero);
}
public static class EnemyFactory
{
    public static Enemy Create(int location) => location switch { 0 => new("Тень в бамбуке", 32, 7, new Aggressive()), 1 => new("Хранитель ручья", 55, 9, new Restoring()), 2 => new("Страж перевала", 80, 12, new Relentless()), 3 => new("Безмолвный властитель", 130, 17, new Relentless()), _ => throw new ArgumentException("Неизвестная локация.") };
}
public sealed class Location(int id, string name, string description, int[] exits)
{
    public int Id { get; } = id; public string Name { get; } = name; public string Description { get; } = description;
    public IReadOnlyList<int> Exits { get; } = Array.AsReadOnly(exits);
    public Enemy Enemy { get; } = EnemyFactory.Create(id);
    public bool Looted
    {
        get; internal set;
    }
}
public enum JourneyState
{
    Exploring, Combat, Victory, Defeat
}
public record HeroData(string ClassId, int Health, int Mana, int Level, int Exp, int Gold, string[] Items, int[] Equipment);
public record SaveData(int Version, HeroData Hero, int Location, JourneyState State, int[] EnemyHealth, bool[] Looted);
public sealed class Journey
{
    public Hero Hero
    {
        get;
    }
    public IReadOnlyList<Location> Locations
    {
        get;
    } = Array.AsReadOnly(new[] {
        new Location(0, "Бамбуковая тропа", "Мягкий свет сквозь листья. Здесь торгует странствующий лекарь.", [1]),
        new Location(1, "Каменный ручей", "Вода обходит камни. За мостом начинается подъём.", [0, 2]),
        new Location(2, "Туманный перевал", "Ветер несёт звон далёкого колокола.", [1, 3]),
        new Location(3, "Безмолвный храм", "Последняя дверь. За ней — источник тумана.", [2]) });
    public int LocationIndex
    {
        get; private set;
    }
    public Location Current => Locations[LocationIndex];
    public JourneyState State
    {
        get; private set;
    }
    public event Action<string>? Happened;
    public Journey(Hero hero)
    {
        Hero = hero;
        hero.LevelUp += level => Happened?.Invoke($"Новый уровень: {level}. Здоровье и мана восстановлены.");
    }
    private void Exploring()
    {
        if (State != JourneyState.Exploring)
            throw new InvalidOperationException("Сначала завершите бой или начните новое путешествие.");
    }
    private void Combat()
    {
        if (State != JourneyState.Combat)
            throw new InvalidOperationException("Сейчас нет боя.");
    }
    public void Move(int location)
    {
        Exploring();
        if (!Current.Exits.Contains(location))
            throw new InvalidOperationException("Нет пути в эту локацию.");
        if (location == 3 && Locations.Take(3).Any(l => l.Enemy.Alive))
            throw new InvalidOperationException("Сначала победите трёх хранителей.");
        LocationIndex = location;
        Happened?.Invoke("Переход: " + Current.Name);
    }
    public void Fight()
    {
        Exploring();
        if (!Current.Enemy.Alive)
            throw new InvalidOperationException("Локация уже очищена.");
        State = JourneyState.Combat;
        Happened?.Invoke("Начался бой: " + Current.Enemy.Name);
    }
    public void Attack()
    {
        Combat();
        var before = Current.Enemy.Health;
        Current.Enemy.TakeDamage(Hero.Strength);
        Happened?.Invoke($"Атака: {before - Current.Enemy.Health} урона.");
        EndTurn();
    }
    public void Ability()
    {
        Combat();
        var message = Hero.Ability(Current.Enemy);
        Happened?.Invoke(message);
        EndTurn();
    }
    public void Use(Item item)
    {
        if (State is JourneyState.Victory or JourneyState.Defeat)
            throw new InvalidOperationException("Путешествие завершено.");
        if (!Hero.Inventory.Contains(item))
            throw new InvalidOperationException("Предмета нет в инвентаре.");
        item.Use(Hero);
        Happened?.Invoke("Использовано: " + item.Name);
        if (State == JourneyState.Combat)
            EndTurn();
    }
    public void Equip(Item item)
    {
        Exploring();
        item.Equip(Hero);
    }
    public void Unequip(Item item)
    {
        Exploring();
        Hero.Unequip(item);
    }
    public void Escape()
    {
        Combat();
        var message = Current.Enemy.TakeTurn(Hero);
        Happened?.Invoke(message);
        State = Hero.Alive ? JourneyState.Exploring : JourneyState.Defeat;
        Happened?.Invoke(Hero.Alive ? "Вы отступили. Враг остался в локации." : "Путешествие окончено.");
    }
    private void EndTurn()
    {
        if (!Current.Enemy.Alive)
        {
            Hero.Reward(15 + LocationIndex * 8, 25 + LocationIndex * 15);
            State = LocationIndex == 3 ? JourneyState.Victory : JourneyState.Exploring;
            Happened?.Invoke(State == JourneyState.Victory ? "Победа. Туман рассеялся." : "Победа в бою. Получены опыт и золото.");
            return;
        }
        var message = Current.Enemy.TakeTurn(Hero);
        Happened?.Invoke(message);
        if (!Hero.Alive)
        {
            State = JourneyState.Defeat;
            Happened?.Invoke("Поражение. Начните новое путешествие или загрузите сохранение.");
        }
    }
    public void Collect()
    {
        Exploring();
        if (Current.Looted)
            throw new InvalidOperationException("Здесь больше нет находок.");
        Current.Looted = true;
        Hero.Add(LocationIndex % 2 == 0 ? new Weapon() : new Armor());
        Hero.Add(new Potion());
        Happened?.Invoke("Найдены снаряжение и травяной настой.");
    }
    public void Buy(string id)
    {
        Exploring();
        if (LocationIndex != 0)
            throw new InvalidOperationException("Торговец находится на Бамбуковой тропе.");
        Hero.Buy(ItemFactory.Create(id));
        Happened?.Invoke("Покупка завершена.");
    }
    public void Save(string path) => Core.Storage.Save(path, new SaveData(1, new(Hero.ClassId, Hero.Health, Hero.Mana, Hero.Level, Hero.Experience, Hero.Gold, Hero.Inventory.Select(i => i.Id).ToArray(), Hero.Equipment.Values.Select(i => Hero.Inventory.ToList().IndexOf(i)).ToArray()), LocationIndex, State, Locations.Select(l => l.Enemy.Health).ToArray(), Locations.Select(l => l.Looted).ToArray()));
    public static Journey Load(string path)
    {
        var data = Core.Storage.Load<SaveData>(path);
        if (data.Hero is null || data.EnemyHealth is null || data.Looted is null || data.Version != 1 || data.Location is < 0 or > 3 || !Enum.IsDefined(data.State) || data.EnemyHealth.Length != 4 || data.Looted.Length != 4)
            throw new InvalidDataException("Некорректное сохранение.");
        var hero = HeroFactory.Create(data.Hero.ClassId);
        hero.Restore(data.Hero);
        var journey = new Journey(hero) { LocationIndex = data.Location, State = data.State };
        for (var i = 0; i < 4; i++)
        {
            journey.Locations[i].Enemy.RestoreHealth(data.EnemyHealth[i]);
            journey.Locations[i].Looted = data.Looted[i];
        }
        if ((data.State == JourneyState.Defeat) != !hero.Alive || (data.State == JourneyState.Combat && !journey.Current.Enemy.Alive) || (data.State == JourneyState.Victory && journey.Locations[3].Enemy.Alive))
            throw new InvalidDataException("Состояние игры противоречит сохранению.");
        return journey;
    }
}
