using DotNetEnv;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using Microsoft.Data.Sqlite;
using Dapper;

namespace RegiBot
{
    enum RegistrationType
    {
        Single,
        Team
    }
    enum Step
    {
        FirstName,
        LastName,
        Age,
        PhoneNumber,
        End
    }
    class UserData
    {
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public int Age { get; set; }
        public string PhoneNumber { get; set; }
    }

    class Registration
    {
        public RegistrationType RegistrationType { get; set; }
        public List<UserData> Users { get; set; } = new List<UserData>();
        public Step CurrentStep { get; set; } = Step.FirstName;
        public int CurrentUser { get; set; } = 0;
    }

    class Program
    {
        private static CancellationTokenSource _cts;
        private static TelegramBotClient _botClient;
        private static string dbConnection;
        private static Dictionary<long, Registration> registrations = new Dictionary<long, Registration>();
        private const int MAX_COMMAND_SIZE = 3;

        static async Task Main(string[] args)
        {
            Env.Load();

            dbConnection = Env.GetString("DB_CONNECTION");

            if (string.IsNullOrEmpty(dbConnection))
            {
                throw new Exception("db connection string not found in environment");
            }

            using (var connection = new SqliteConnection($"Data Source={dbConnection}"))
            {
                await connection.ExecuteAsync(@"
                    CREATE TABLE IF NOT EXISTS Users (
                        Id          INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL,
                        FirstName   TEXT    NOT NULL,
                        LastName    TEXT    NOT NULL,
                        Age         INTEGER NOT NULL,
                        PhoneNumber TEXT    NOT NULL,
                        TeamId      TEXT
                    );");
            }

            _cts = new CancellationTokenSource();
            _botClient = new TelegramBotClient(Env.GetString("BOT_TOKEN"), cancellationToken: _cts.Token);

            await _botClient.DeleteWebhook();
            await _botClient.DropPendingUpdates();

            _botClient.OnMessage += MessageHandler;

            var me = await _botClient.GetMe();

            Console.WriteLine($"Bot {me.Username} is started... Press escape to terminate");

            while (Console.ReadKey(true).Key != ConsoleKey.Escape) { }

            _cts.Cancel();
        }

        private static async Task MessageHandler(Message message, UpdateType type)
        {
            if (message.Text == "/start")
            {
                string text = "Выберите тип регистрации:\n" +
                    "1 - Одиночная регистрация\n" +
                    "2 - Регистрация команды (до 3 человек)";
                await _botClient.SendMessage(message.Chat.Id, text);
                return;
            }

            if (!registrations.ContainsKey(message.Chat.Id))
            {
                if (message.Text == "1")
                {
                    registrations[message.Chat.Id] = new Registration()
                    {
                        RegistrationType = RegistrationType.Single,
                        Users = { new UserData() }
                    };
                    await _botClient.SendMessage(message.Chat.Id, "Введите ваше имя");
                }
                else if (message.Text == "2")
                {
                    registrations[message.Chat.Id] = new Registration()
                    {
                        RegistrationType = RegistrationType.Team,
                        Users = { new UserData() }
                    };
                    await _botClient.SendMessage(message.Chat.Id, "Введите имя 1-го участника");
                }
                return;
            }

            await UserRegistration(message);
        }

        private static async Task UserRegistration(Message message)
        {
            var data = registrations[message.Chat.Id];
            var currentUser = data.Users[data.CurrentUser];

            switch (data.CurrentStep)
            {
                case Step.FirstName:
                    currentUser.FirstName = message.Text;
                    data.CurrentStep = Step.LastName;

                    if (data.RegistrationType == RegistrationType.Single)
                    {
                        await _botClient.SendMessage(message.Chat.Id, "Введите вашу фамилию");
                    }
                    else if (data.RegistrationType == RegistrationType.Team)
                    {
                        await _botClient.SendMessage(message.Chat.Id, $"Введите фамилию {data.CurrentUser + 1}-го участника");
                    }
                    break;
                case Step.LastName:
                    currentUser.LastName = message.Text;
                    data.CurrentStep = Step.Age;

                    if (data.RegistrationType == RegistrationType.Single)
                    {
                        await _botClient.SendMessage(message.Chat.Id, "Введите ваш возраст");
                    }
                    else if (data.RegistrationType == RegistrationType.Team)
                    {
                        await _botClient.SendMessage(message.Chat.Id, $"Введите возраст {data.CurrentUser + 1}-го участника");
                    }
                    break;
                case Step.Age:
                    if (int.TryParse(message.Text, out int age))
                    {
                        currentUser.Age = age;
                        data.CurrentStep = Step.PhoneNumber;
                        if (data.RegistrationType == RegistrationType.Single)
                        {
                            await _botClient.SendMessage(message.Chat.Id, "Введите ваш номер телефона");
                        }
                        else if (data.RegistrationType == RegistrationType.Team)
                        {
                            await _botClient.SendMessage(message.Chat.Id, $"Введите номер телефона {data.CurrentUser + 1}-го участника");
                        }
                    }
                    else
                    {
                        await _botClient.SendMessage(message.Chat.Id, "Пожалуйста, введите корректный возраст (число)");
                    }
                    break;
                case Step.PhoneNumber:
                    currentUser.PhoneNumber = message.Text;

                    if (data.RegistrationType == RegistrationType.Team &&
                        data.Users.Count < MAX_COMMAND_SIZE &&
                        data.CurrentUser < MAX_COMMAND_SIZE - 1)
                    {
                        ReplyKeyboardMarkup keyboard = new ReplyKeyboardMarkup(
                        new[]
                        {
                            new KeyboardButton("Да"),
                            new KeyboardButton("Нет")
                        }
                        ) { ResizeKeyboard = true };
                        await _botClient.SendMessage(message.Chat.Id, "Добавить еще одного участника? (максимум 3)", replyMarkup: keyboard);
                        data.CurrentStep = Step.End;
                    }
                    else
                    {
                        await CompleteRegistration(message.Chat.Id);
                    }
                    break;
                case Step.End:
                    if (message.Text == "Да")
                    {
                        data.Users.Add(new UserData());
                        data.CurrentUser++;
                        data.CurrentStep = Step.FirstName;

                        await _botClient.SendMessage(message.Chat.Id,
                            $"Введите имя {data.CurrentUser + 1}-го участника",
                            replyMarkup: new ReplyKeyboardRemove());
                    }
                    else
                    {
                        await CompleteRegistration(message.Chat.Id);
                    }
                    break;
            }
        }

        private static async Task CompleteRegistration(long chatId)
        {
            var data = registrations[chatId];
            string? teamId = data.RegistrationType == RegistrationType.Team ? Guid.NewGuid().ToString() : null;

            for (int i = 0; i < data.Users.Count; i++)
            {
                await SaveUserToDatabase(data.Users[i], teamId);
            }

            registrations.Remove(chatId);

            string text = data.RegistrationType == RegistrationType.Single ? "Спасибо, вы зарегистрированы!" : "Спасибо, ваша команда зарегистрирована!";
            await _botClient.SendMessage(chatId, text, replyMarkup: new ReplyKeyboardRemove());
        }

        private static async Task SaveUserToDatabase(UserData user, string? teamId)
        {
            using (var connection = new SqliteConnection($"Data Source={dbConnection}"))
            {
                await connection.ExecuteAsync("INSERT INTO Users (FirstName, LastName, Age, PhoneNumber, TeamId) " +
                    "VALUES (@FirstName, @LastName, @Age, @PhoneNumber, @TeamId)",
                    new { user.FirstName, user.LastName, user.Age, user.PhoneNumber, TeamId = teamId });
            }
        }
    }
}
