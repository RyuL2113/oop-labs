using Zen.Ui;
namespace Zen.Chat;

public sealed class LoginForm : ZenForm
{
    public LoginForm() : base("Место для разговора", "03 / ОНЛАЙН-ЧАТ     ·     Сначала — соединиться")
    {
        var host = Input("Адрес сервера");
        host.Text = "127.0.0.1";
        var port = new NumericUpDown { Minimum = 1, Maximum = 65535, Value = 5050, Width = 100 };
        var nick = Input("Ваше имя");
        var connect = Button("Войти", () => { }, true);
        connect.Click += async (_, _) =>
        {
            connect.Enabled = false;
            await GuardAsync(async () =>
            {
                var client = new ChatClient();
                try
                {
                    await client.ConnectAsync(host.Text.Trim(), (int)port.Value, nick.Text);
                }
                catch { client.Dispose(); throw; }
                var chat = new ChatForm(client, nick.Text.Trim());
                chat.FormClosed += (_, _) => Show();
                chat.Show();
                Hide();
            });
            connect.Enabled = true;
        };
        Body.Controls.Add(Caption("АДРЕС     /     ПОРТ     /     ИМЯ"));
        Body.Controls.Add(Row(host, port, nick, connect));
        Body.Controls.Add(Caption("Сначала запустите ChatServer. Для другого компьютера укажите его IP-адрес. Порт по умолчанию: 5050."));
        AcceptButton = connect;
    }
}
public sealed class ChatForm : ZenForm
{
    public ChatForm(ChatClient client, string nick) : base("Разговор", "03 / ОНЛАЙН-ЧАТ     ·     " + nick)
    {
        var log = new RichTextBox { ReadOnly = true, Width = 740, Height = 400, BorderStyle = BorderStyle.None, BackColor = Paper, Font = Font };
        var users = new ListBox { Width = 200, Height = 400, BorderStyle = BorderStyle.None, BackColor = Stone };
        users.Items.Add("Всем");
        users.SelectedIndex = 0;
        var text = Input("Напишите сообщение…", 740);
        var status = Caption("В сети · выберите участника для личного сообщения");
        async Task Send()
        {
            await client.SendAsync(text.Text, users.SelectedIndex > 0 ? users.SelectedItem?.ToString() : null);
            text.Clear();
            text.Focus();
        }
        var send = Button("Отправить", () => { }, true);
        send.Click += async (_, _) => await GuardAsync(Send);
        text.KeyDown += async (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await GuardAsync(Send); } };
        Body.Controls.Add(Row(log, users));
        Body.Controls.Add(Row(text, send));
        Body.Controls.Add(status);
        client.MessageReceived += message => OnUi(() =>
        {
            if (message.Type == "users")
            {
                var selected = users.SelectedItem?.ToString();
                users.Items.Clear();
                users.Items.Add("Всем");
                users.Items.AddRange(message.Users ?? []);
                users.SelectedIndex = Math.Max(0, users.Items.IndexOf(selected ?? "Всем"));
                return;
            }
            log.SelectionStart = log.TextLength;
            log.SelectionColor = message.Recipient != null ? Red : message.Type is "system" or "error" ? Matcha : Ink;
            log.AppendText($"{(message.Time == default ? DateTime.Now : message.Time.ToLocalTime()):HH:mm}  {(message.Recipient != null ? "[личное → " + message.Recipient + "] " : "")}{message.Nick}  {message.Text}\n\n");
            log.ScrollToCaret();
        });
        client.Disconnected += reason => OnUi(() => { status.Text = "Не в сети · " + reason; send.Enabled = false; text.Enabled = false; });
        Shown += async (_, _) => await client.ListenAsync();
        FormClosed += (_, _) => client.Dispose();
    }
}
