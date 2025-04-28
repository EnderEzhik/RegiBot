using DotNetEnv;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using Microsoft.Data.Sqlite;
using Dapper;

namespace RegiBot
{
    class Program
    {
        private static string dbConnection = null!;

        private static CancellationTokenSource _cts = null!;
        private static TelegramBotClient _botClient = null!;

        private static Dictionary<long, Registration> registrations = new Dictionary<long, Registration>();
        private const int MAX_COMMAND_SIZE = 3;

        static async Task Main(string[] args)
        {
            Env.Load();

            InitializeDatabase();

            await InitializeBot();
            await RunBot();
        }

        private static void InitializeDatabase()
        {
            dbConnection = Env.GetString("DB_CONNECTION");

            if (string.IsNullOrEmpty(dbConnection))
            {
                throw new ArgumentNullException("DB connection string is null or empty");
            }

            using (var connection = new SqliteConnection($"Data Source={dbConnection}"))
            {
                connection.Execute(@"
                    CREATE TABLE IF NOT EXISTS Users (
                        Id          INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL,
                        FirstName   TEXT    NOT NULL,
                        LastName    TEXT    NOT NULL,
                        Age         INTEGER NOT NULL,
                        PhoneNumber TEXT    NOT NULL UNIQUE,
                        TeamId      TEXT
                    );");
            }
        }

        private static async Task InitializeBot()
        {
            string BOT_TOKEN = Env.GetString("BOT_TOKEN");

            if (string.IsNullOrEmpty(BOT_TOKEN))
            {
                throw new ArgumentNullException("bot token is null or empty");
            }

            _cts = new CancellationTokenSource();
            _botClient = new TelegramBotClient(BOT_TOKEN, cancellationToken: _cts.Token);

            await _botClient.DeleteWebhook();
            await _botClient.DropPendingUpdates();

            _botClient.OnMessage += MessageHandler;
        }

        private static async Task RunBot()
        {
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
                    await _botClient.SendMessage(message.Chat.Id, "Одиночная регистрация");

                    registrations[message.Chat.Id] = new Registration()
                    {
                        RegistrationType = RegistrationType.Single,
                        Users = { new UserData() }
                    };
                    await _botClient.SendMessage(message.Chat.Id, "Введите ваше имя");
                }
                else if (message.Text == "2")
                {
                    await _botClient.SendMessage(message.Chat.Id, "Регистрация команды");

                    registrations[message.Chat.Id] = new Registration()
                    {
                        RegistrationType = RegistrationType.Team,
                        Users = { new UserData() }
                    };
                    await _botClient.SendMessage(message.Chat.Id, "Введите имя 1-го участника");
                }
                return;
            }

            if (message.Text == "/cancel")
            {
                registrations.Remove(message.Chat.Id);

                await _botClient.SendMessage(message.Chat.Id, "Регистрация отменена", replyMarkup: new ReplyKeyboardRemove());
                return;
            }

            await UserRegistration(message);
        }

        private static async Task UserRegistration(Message message)
        {
            var registration = registrations[message.Chat.Id];
            var currentUser = registration.Users[registration.CurrentUserIndex];

            switch (registration.CurrentStep)
            {
                case RegistrationStep.FirstName:
                    currentUser.FirstName = message.Text;
                    registration.CurrentStep = RegistrationStep.LastName;

                    if (registration.RegistrationType == RegistrationType.Single)
                    {
                        await _botClient.SendMessage(message.Chat.Id, "Введите вашу фамилию");
                    }
                    else
                    {
                        await _botClient.SendMessage(message.Chat.Id, $"Введите фамилию {registration.CurrentUserIndex + 1}-го участника");
                    }
                    break;
                case RegistrationStep.LastName:
                    currentUser.LastName = message.Text;
                    registration.CurrentStep = RegistrationStep.Age;

                    if (registration.RegistrationType == RegistrationType.Single)
                    {
                        await _botClient.SendMessage(message.Chat.Id, "Введите ваш возраст");
                    }
                    else
                    {
                        await _botClient.SendMessage(message.Chat.Id, $"Введите возраст {registration.CurrentUserIndex + 1}-го участника");
                    }
                    break;
                case RegistrationStep.Age:
                    if (int.TryParse(message.Text, out int age))
                    {
                        currentUser.Age = age;
                        registration.CurrentStep = RegistrationStep.PhoneNumber;

                        if (registration.RegistrationType == RegistrationType.Single)
                        {
                            await _botClient.SendMessage(message.Chat.Id, "Введите ваш номер телефона");
                        }
                        else
                        {
                            await _botClient.SendMessage(message.Chat.Id, $"Введите номер телефона {registration.CurrentUserIndex + 1}-го участника");
                        }
                    }
                    else
                    {
                        await _botClient.SendMessage(message.Chat.Id, "Пожалуйста, введите корректный возраст (число)");
                    }
                    break;
                case RegistrationStep.PhoneNumber:
                    if (await PhoneNumberRegistered(message.Text, registration))
                    {
                        await _botClient.SendMessage(message.Chat.Id, "Этот номер телефона уже зарегистрирован. Пожалуйста, введите другой");
                        break;
                    }

                    currentUser.PhoneNumber = message.Text;

                    if (registration.RegistrationType == RegistrationType.Team &&
                        registration.Users.Count < MAX_COMMAND_SIZE)
                    {
                        registration.CurrentStep = RegistrationStep.NextUser;

                        ReplyKeyboardMarkup keyboard = new ReplyKeyboardMarkup(
                        new[]
                        {
                            new KeyboardButton("Да"),
                            new KeyboardButton("Нет")
                        }
                        )
                        { ResizeKeyboard = true };
                        await _botClient.SendMessage(message.Chat.Id, "Добавить еще одного участника? (максимум 3)", replyMarkup: keyboard);
                    }
                    else
                    {
                        await CompleteRegistration(message.Chat.Id);
                    }
                    break;
                case RegistrationStep.NextUser:
                    if (message.Text == "Да")
                    {
                        registration.Users.Add(new UserData());
                        registration.CurrentUserIndex++;
                        registration.CurrentStep = RegistrationStep.FirstName;

                        await _botClient.SendMessage(message.Chat.Id,
                            $"Введите имя {registration.CurrentUserIndex + 1}-го участника",
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
            var registration = registrations[chatId];
            string? teamId = registration.RegistrationType == RegistrationType.Team ? Guid.NewGuid().ToString() : null;

            for (int i = 0; i < registration.Users.Count; i++)
            {
                await SaveUserToDatabase(registration.Users[i], teamId);
            }

            registrations.Remove(chatId);

            string text = registration.RegistrationType == RegistrationType.Single ? "Спасибо, вы зарегистрированы!" : "Спасибо, ваша команда зарегистрирована!";
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

        private static async Task<bool> PhoneNumberRegistered(string phoneNumber, Registration registration)
        {
            if (registration.Users.Select(u => u.PhoneNumber).Contains(phoneNumber))
            {
                return true;
            }

            using var connection = new SqliteConnection($"Data Source={dbConnection}");
            var result = await connection.QueryAsync<int>($"SELECT COUNT(*) FROM Users WHERE PhoneNumber=@phoneNumber", new { phoneNumber });

            if (result.First() > 0)
            {
                return true;
            }

            return false;
        }
    }
}
