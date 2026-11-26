using Zen.Ui;
namespace Zen.Battleship;

public sealed class SeaPanel : Panel
{
    public SeaPanel()
    {
        DoubleBuffered = true;
        Size = new Size(390, 390);
    }
}
public sealed class MainForm : ZenForm
{
    private Board _board = Board.Random();
    private Game? _game;
    private Player? _opponent;
    private Connection? _connection;
    private readonly SeaPanel _own = new(), _enemy = new();
    private readonly Label _status = Caption("Флот готов. Выберите игру с компьютером или подключитесь по сети.");
    private readonly TextBox _host = Input("Адрес", 170);
    private readonly NumericUpDown _port = new() { Minimum = 1, Maximum = 65535, Value = 5051, Width = 90 };
    private bool _manual, _vertical, _busy, _hostSide, _peerReady, _ready;
    private Position? _hover;
    private readonly CancellationTokenSource _stop = new();
    public MainForm() : base("Между берегами", "04 / МОРСКОЙ БОЙ     ·     Два поля, один спокойный ход")
    {
        _host.Text = "127.0.0.1";
        Body.Controls.Add(Row(Button("Автофлот", () => { EnsurePlacing(); _board = Board.Random(); _manual = false; RefreshBoards(); }), Button("Вручную", () => { EnsurePlacing(); _board = new(); _manual = true; RefreshBoards(); }), Button("Повернуть ↻", () => { _vertical = !_vertical; _own.Invalidate(); }), Button("С компьютером", StartBot, true), Button("Новая партия", NewGame)));
        var create = Button("Создать", () => { });
        create.Click += async (_, _) => await Connect(true);
        var join = Button("Войти", () => { });
        join.Click += async (_, _) => await Connect(false);
        var ready = Button("Готов", () => { });
        ready.Click += async (_, _) => await GuardAsync(Ready);
        Body.Controls.Add(Row(_host, _port, create, join, ready));
        Body.Controls.Add(Row(new Label { Text = "ВАШ БЕРЕГ", Width = 390, Height = 28 }, new Label { Text = "БЕРЕГ СОПЕРНИКА", Width = 390, Height = 28 }));
        Body.Controls.Add(Row(_own, _enemy));
        Body.Controls.Add(_status);
        _own.Paint += (_, e) => DrawBoard(e.Graphics, false);
        _enemy.Paint += (_, e) => DrawBoard(e.Graphics, true);
        _own.MouseMove += (_, e) => { _hover = Coordinate(e.Location); _own.Invalidate(); };
        _own.MouseLeave += (_, _) => { _hover = null; _own.Invalidate(); };
        _own.MouseClick += (_, e) => Guard(() => { if (!_manual || _board.Ready) return; EnsurePlacing(); if (Coordinate(e.Location) is { } p) _board.Place(p, Board.Fleet[_board.Ships.Count], _vertical); RefreshBoards(); });
        _enemy.MouseClick += async (_, e) => { if (Coordinate(e.Location) is { } p) await GuardAsync(() => Shoot(p)); };
        FormClosed += (_, _) => { _stop.Cancel(); _connection?.Dispose(); };
    }
    private void EnsurePlacing()
    {
        if (_game != null || _ready)
            throw new RuleException("Сначала начните новую партию.");
    }
    private void StartBot()
    {
        EnsurePlacing();
        if (!_board.Ready)
            throw new RuleException("Расставьте все 10 кораблей.");
        _connection?.Dispose();
        _connection = null;
        _opponent = new BotPlayer(Board.Random());
        _game = new Game(_board);
        _game.Start(true);
        RefreshBoards();
    }
    private async Task Connect(bool host)
    {
        if (_busy)
            return;
        _busy = true;
        await GuardAsync(async () =>
        {
            EnsurePlacing();
            _connection?.Dispose();
            _connection = null;
            _peerReady = _ready = false;
            _status.Text = host ? "Ожидание соперника… (до 60 секунд)" : "Подключение…";
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(60));
            var connection = host ? await Connection.HostAsync((int)_port.Value, timeout.Token) : await Connection.JoinAsync(_host.Text, (int)_port.Value, timeout.Token);
            _connection = connection;
            _hostSide = host;
            _opponent = new NetworkPlayer(connection);
            connection.Received += packet => OnUi(async () => { if (_connection == connection) await GuardAsync(() => HandlePacket(packet)); });
            connection.Disconnected += reason => OnUi(() => { if (_connection != connection) return; _status.Text = reason + " Начните новую партию."; _opponent = null; });
            _ = connection.ListenAsync();
            _status.Text = "Соединение установлено. Оба игрока должны нажать «Готов».";
        });
        _busy = false;
    }
    private async Task Ready()
    {
        if (_connection == null)
            throw new RuleException("Сначала установите соединение.");
        if (!_board.Ready)
            throw new RuleException("Расставьте все 10 кораблей.");
        if (_game != null || _ready)
            throw new RuleException("Готовность уже подтверждена.");
        _ready = true;
        await _connection.SendAsync(new("ready"));
        TryStart();
    }
    private void TryStart()
    {
        if (_ready && _peerReady && _game == null)
        {
            _game = new Game(_board);
            _game.Start(_hostSide);
            RefreshBoards();
        }
        else
            _status.Text = "Ждём готовности соперника…";
    }
    private async Task HandlePacket(Packet packet)
    {
        switch (packet.Type)
        {
            case "ready":
                _peerReady = true;
                TryStart();
                break;
            case "new":
                Reset();
                _status.Text = "Соперник предложил новую партию. Расставьте флот и нажмите «Готов».";
                break;
            case "shot":
                var result = (_game ?? throw new RuleException("Игра ещё не началась.")).ReceiveShot(packet.Position);
                await _connection!.SendAsync(new("result", Report: result));
                RefreshBoards();
                break;
            default:
                throw new RuleException("Неизвестный пакет.");
        }
    }
    private async Task Shoot(Position p)
    {
        if (_busy)
            return;
        var game = _game ?? throw new RuleException("Сначала начните игру.");
        var opponent = _opponent ?? throw new RuleException("Нет соперника.");
        game.ValidateShot(p);
        _busy = true;
        try
        {
            var report = await opponent.ReceiveShotAsync(p);
            game.ApplyShot(p, report);
            RefreshBoards();
            while (game.State == GameState.EnemyTurn && opponent.ChooseShot() is { } shot)
            {
                await Task.Delay(300, _stop.Token);
                var reply = game.ReceiveShot(shot);
                opponent.Observe(shot, reply);
                RefreshBoards();
            }
        }
        finally { _busy = false; }
    }
    private void NewGame()
    {
        if (_busy)
            throw new RuleException("Дождитесь окончания текущего действия.");
        Reset();
        if (_connection != null)
            _ = GuardAsync(() => _connection.SendAsync(new("new")));
    }
    private void Reset()
    {
        _game = null;
        _ready = _peerReady = false;
        _board = Board.Random();
        _manual = false;
        RefreshBoards();
    }
    private void RefreshBoards()
    {
        _own.Invalidate();
        _enemy.Invalidate();
        _status.Text = _game?.State switch
        {
            GameState.MyTurn => "Ваш ход · выберите клетку на правом поле",
            GameState.EnemyTurn => "Ход соперника",
            GameState.Over => _game.Won ? "Победа. Можно начать новую партию." : "Поражение. Можно начать новую партию.",
            _ => _board.Ready ? "Флот готов · 10 кораблей. Выберите режим или нажмите «Готов»." : $"Расстановка · следующий корабль: {Board.Fleet[_board.Ships.Count]} палуб. Кнопка «Повернуть» меняет направление."
        };
    }
    private static Position? Coordinate(Point p)
    {
        var x = (p.X - 26) / 34;
        var y = (p.Y - 26) / 34;
        return p.X >= 26 && p.Y >= 26 && Board.Inside(new(x, y)) ? new(x, y) : null;
    }
    private void DrawBoard(Graphics g, bool enemy)
    {
        g.Clear(Paper);
        using var line = new Pen(Color.FromArgb(211, 207, 199));
        using var ink = new SolidBrush(Ink);
        for (var i = 0; i < 10; i++)
        {
            g.DrawString(((char)('А' + i)).ToString(), Font, ink, 33 + i * 34, 2);
            g.DrawString((i + 1).ToString(), Font, ink, 0, 32 + i * 34);
        }
        for (var x = 0; x < 10; x++)
            for (var y = 0; y < 10; y++)
            {
                var cell = enemy ? _game?.EnemyAt(new(x, y)) ?? Cell.Empty : _board.At(new(x, y));
                using var brush = new SolidBrush(cell switch
                {
                    Cell.Ship => Matcha,
                    Cell.Hit or Cell.Sunk => Red,
                    _ => Stone
                });
                var r = new Rectangle(26 + x * 34, 26 + y * 34, 32, 32);
                g.FillRectangle(brush, r);
                g.DrawRectangle(line, r);
                if (cell == Cell.Miss)
                    g.FillEllipse(ink, r.X + 14, r.Y + 14, 4, 4);
                if (cell == Cell.Sunk)
                {
                    g.DrawLine(line, r.Left + 8, r.Top + 8, r.Right - 8, r.Bottom - 8);
                    g.DrawLine(line, r.Right - 8, r.Top + 8, r.Left + 8, r.Bottom - 8);
                }
            }
        if (!enemy && _manual && !_board.Ready && _hover is { } hover)
        {
            var length = Board.Fleet[_board.Ships.Count];
            using var preview = new SolidBrush(Color.FromArgb(130, _board.CanPlace(hover, length, _vertical) ? Matcha : Red));
            foreach (var p in Board.Placement(hover, length, _vertical).Where(Board.Inside))
                g.FillRectangle(preview, 26 + p.X * 34, 26 + p.Y * 34, 32, 32);
        }
    }
}

