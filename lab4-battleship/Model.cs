namespace Zen.Battleship;

public enum Cell
{
    Empty, Ship, Miss, Hit, Sunk
}
public enum ShotResult
{
    Miss, Hit, Sunk
}
public enum GameState
{
    Placing, MyTurn, EnemyTurn, Over
}
public readonly record struct Position(int X, int Y);
public sealed class RuleException(string message) : InvalidOperationException(message);
public sealed class Ship
{
    private readonly HashSet<Position> _hits = [];
    private readonly Position[] _cells;
    public IReadOnlyList<Position> Cells => Array.AsReadOnly(_cells);
    public bool IsSunk => _hits.Count == _cells.Length;
    public Ship(IEnumerable<Position> cells) => _cells = cells.ToArray();
    public void Hit(Position p)
    {
        if (!_cells.Contains(p))
            throw new RuleException("Выстрел вне корабля.");
        _hits.Add(p);
    }
}
public record ShotReport(ShotResult Result, Position[] SunkCells, bool Defeated);
public sealed class Board
{
    public const int Size = 10;
    public static IReadOnlyList<int> Fleet { get; } = Array.AsReadOnly(new[] { 4, 3, 3, 2, 2, 2, 1, 1, 1, 1 });
    private readonly Cell[,] _cells = new Cell[Size, Size];
    private readonly List<Ship> _ships = [];
    public IReadOnlyList<Ship> Ships => _ships.AsReadOnly();
    public bool Ready => _ships.Count == Fleet.Count;
    public bool Defeated => Ready && _ships.All(s => s.IsSunk);
    public static bool Inside(Position p) => p.X is >= 0 and < Size && p.Y is >= 0 and < Size;
    public Cell At(Position p)
    {
        Validate(p);
        return _cells[p.X, p.Y];
    }
    private static void Validate(Position p)
    {
        if (!Inside(p))
            throw new RuleException("Клетка за пределами поля.");
    }
    public static Position[] Placement(Position start, int length, bool vertical) => Enumerable.Range(0, length).Select(i => new Position(start.X + (vertical ? 0 : i), start.Y + (vertical ? i : 0))).ToArray();
    public bool CanPlace(Position start, int length, bool vertical)
    {
        if (!Fleet.Contains(length) || _ships.Count(s => s.Cells.Count == length) >= Fleet.Count(n => n == length))
            return false;
        return Placement(start, length, vertical).All(p => Inside(p) && Neighbors(p).All(n => _cells[n.X, n.Y] == Cell.Empty));
    }
    public void Place(Position start, int length, bool vertical)
    {
        if (!CanPlace(start, length, vertical))
            throw new RuleException("Корабль выходит за поле, касается другого или превышает состав флота.");
        var cells = Placement(start, length, vertical);
        _ships.Add(new Ship(cells));
        foreach (var p in cells)
            _cells[p.X, p.Y] = Cell.Ship;
    }
    public static IEnumerable<Position> Neighbors(Position p)
    {
        for (var x = p.X - 1; x <= p.X + 1; x++)
            for (var y = p.Y - 1; y <= p.Y + 1; y++)
            {
                var n = new Position(x, y);
                if (Inside(n))
                    yield return n;
            }
    }
    public static Board Random(Random? random = null)
    {
        random ??= System.Random.Shared;
        for (var retry = 0; retry < 1000; retry++)
        {
            var board = new Board();
            foreach (var size in Fleet)
            {
                var options = (from x in Enumerable.Range(0, Size) from y in Enumerable.Range(0, Size) from vertical in new[] { true, false } where board.CanPlace(new(x, y), size, vertical) select (new Position(x, y), vertical)).ToArray();
                if (options.Length == 0)
                    break;
                var option = options[random.Next(options.Length)];
                board.Place(option.Item1, size, option.vertical);
            }
            if (board.Ready)
                return board;
        }
        throw new RuleException("Не удалось расставить флот.");
    }
    public ShotReport Shoot(Position p)
    {
        Validate(p);
        if (!Ready)
            throw new RuleException("Флот ещё не расставлен.");
        if (At(p) is Cell.Miss or Cell.Hit or Cell.Sunk)
            throw new RuleException("В эту клетку уже стреляли.");
        var ship = _ships.Find(s => s.Cells.Contains(p));
        if (ship == null)
        {
            _cells[p.X, p.Y] = Cell.Miss;
            return new(ShotResult.Miss, [], false);
        }
        ship.Hit(p);
        _cells[p.X, p.Y] = Cell.Hit;
        if (!ship.IsSunk)
            return new(ShotResult.Hit, [], false);
        foreach (var cell in ship.Cells)
        {
            _cells[cell.X, cell.Y] = Cell.Sunk;
            foreach (var n in Neighbors(cell))
                if (At(n) == Cell.Empty)
                    _cells[n.X, n.Y] = Cell.Miss;
        }
        return new(ShotResult.Sunk, ship.Cells.ToArray(), Defeated);
    }
}
public sealed class Game
{
    private readonly Cell[,] _enemy = new Cell[10, 10];
    public Board Own
    {
        get;
    }
    public GameState State { get; private set; } = GameState.Placing;
    public bool Won
    {
        get; private set;
    }
    public Game(Board own) => Own = own;
    public Cell EnemyAt(Position p) => Board.Inside(p) ? _enemy[p.X, p.Y] : throw new RuleException("Клетка вне поля.");
    public void Start(bool first)
    {
        if (State != GameState.Placing || !Own.Ready)
            throw new RuleException("Расставьте весь флот.");
        State = first ? GameState.MyTurn : GameState.EnemyTurn;
    }
    public void ValidateShot(Position p)
    {
        if (State != GameState.MyTurn)
            throw new RuleException("Сейчас нельзя стрелять.");
        if (EnemyAt(p) != Cell.Empty)
            throw new RuleException("В эту клетку уже стреляли.");
    }
    public void ApplyShot(Position p, ShotReport report)
    {
        ValidateShot(p);
        if (!Enum.IsDefined(report.Result) || report.SunkCells is null || report.SunkCells.Any(c => !Board.Inside(c)) ||
            (report.Result == ShotResult.Sunk && (report.SunkCells.Length is < 1 or > 4 || !report.SunkCells.Contains(p))) ||
            (report.Result != ShotResult.Sunk && (report.SunkCells.Length > 0 || report.Defeated)))
            throw new RuleException("Некорректный ответ соперника.");
        _enemy[p.X, p.Y] = report.Result == ShotResult.Miss ? Cell.Miss : Cell.Hit;
        foreach (var c in report.SunkCells)
        {
            _enemy[c.X, c.Y] = Cell.Sunk;
            foreach (var n in Board.Neighbors(c))
                if (_enemy[n.X, n.Y] == Cell.Empty)
                    _enemy[n.X, n.Y] = Cell.Miss;
        }
        if (report.Defeated)
        {
            Won = true;
            State = GameState.Over;
        }
        else if (report.Result == ShotResult.Miss)
            State = GameState.EnemyTurn;
    }
    public ShotReport ReceiveShot(Position p)
    {
        if (State != GameState.EnemyTurn)
            throw new RuleException("Сейчас не ход соперника.");
        var report = Own.Shoot(p);
        if (report.Defeated)
            State = GameState.Over;
        else if (report.Result == ShotResult.Miss)
            State = GameState.MyTurn;
        return report;
    }
}
public abstract class Player
{
    public abstract Task<ShotReport> ReceiveShotAsync(Position p);
    public virtual Position? ChooseShot() => null;
    public virtual void Observe(Position p, ShotReport report)
    {
    }
}
public sealed class LocalPlayer(Func<Position, ShotReport> receive) : Player
{
    public override Task<ShotReport> ReceiveShotAsync(Position p) => Task.FromResult(receive(p));
}
public sealed class BotPlayer(Board board) : Player
{
    private readonly HashSet<Position> _tried = [];
    private readonly Queue<Position> _targets = new();
    public override Task<ShotReport> ReceiveShotAsync(Position p) => Task.FromResult(board.Shoot(p));
    public override Position? ChooseShot()
    {
        while (_targets.TryDequeue(out var target))
            if (_tried.Add(target))
                return target;
        var available = (from x in Enumerable.Range(0, 10) from y in Enumerable.Range(0, 10) let p = new Position(x, y) where !_tried.Contains(p) select p).ToArray();
        if (available.Length == 0)
            return null;
        var choice = available[System.Random.Shared.Next(available.Length)];
        _tried.Add(choice);
        return choice;
    }
    public override void Observe(Position p, ShotReport report)
    {
        if (report.Result == ShotResult.Hit)
            foreach (var n in new[] { new Position(p.X - 1, p.Y), new(p.X + 1, p.Y), new(p.X, p.Y - 1), new(p.X, p.Y + 1) })
                if (Board.Inside(n) && !_tried.Contains(n))
                    _targets.Enqueue(n);
        if (report.Result == ShotResult.Sunk)
        {
            _targets.Clear();
            foreach (var c in report.SunkCells)
                foreach (var n in Board.Neighbors(c))
                    _tried.Add(n);
        }
    }
}
