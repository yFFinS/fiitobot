﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.InlineQueryResults;
using Telegram.Bot.Types.InputFiles;
using Telegram.Bot.Types.ReplyMarkups;

namespace fiitobot.Services
{
    [Flags]
    public enum ContactDetailsLevel
    {
        No = 0,
        Minimal = 1,
        Contacts = 2,
        Marks = 4,
        SecretNote = 8,
        LinksToFiitTeamFiles = 16,
        TechnicalInfo = 32,
        Details = 64,
        Iddqd = 255,
    }

    public interface IPresenter
    {
        Task Say(string text, long chatId);
        Task ShowContact(Contact contact, long chatId, ContactDetailsLevel detailsLevel);
        Task ShowPhoto(Contact contact, PersonPhoto photo, long chatId, ContactType senderType);
        Task ShowOtherResults(Contact[] otherContacts, long chatId);
        Task SayNoResults(long chatId);
        Task SayNoRights(long chatId, ContactType senderType);
        Task SayBeMoreSpecific(long chatId);
        Task InlineSearchResults(string inlineQueryId, Contact[] foundContacts);
        Task ShowDetails(PersonData person, string[] sources, long chatId);
        Task SayReloadStarted(long chatId);
        Task SayReloaded(BotData botData, long chatId);
        Task ShowErrorToDevops(Update incomingUpdate, string errorMessage);
        Task ShowHelp(long chatId, ContactType senderType);
        Task ShowContactsBy(string criteria, IList<Contact> people, long chatId);
        Task ShowDownloadContactsYearSelection(long chatId);
        Task ShowDownloadContactsSuffixSelection(long chatId, string year);
        Task SendContacts(long chatId, byte[] content, string filename);
        Task SayUploadPhotoFirst(long chatId);
        Task ShowPhotoForModeration(long moderatorChatId, Contact contact, Stream contactNewPhoto);
        Task SayPhotoGoesToModeration(long chatId, Stream photo);
        Task SayPhotoAccepted(Contact photoOwner, Contact moderator, long chatId);
        Task SayPhotoRejected(Contact photoOwner, Contact moderator, long chatId);
        Task ShowPhoto(Contact personContact, byte[] photoBytes, long chatId);
        Task PromptChangePhoto(long chatId);
        Task OfferToSetHisPhoto(long chatId);
    }

    public class Presenter : IPresenter
    {
        private readonly ITelegramBotClient botClient;
        private readonly Settings settings;

        public Presenter(ITelegramBotClient botClient, Settings settings)
        {
            this.botClient = botClient;
            this.settings = settings;
        }

        public async Task InlineSearchResults(string inlineQueryId, Contact[] foundContacts)
        {
            var results = foundContacts.Select(c =>
                new InlineQueryResultArticle(c.GetHashCode().ToString(), $"{c.LastName} {c.FirstName} {c.FormatMnemonicGroup(DateTime.Now)} {c.Telegram}",
                    new InputTextMessageContent(FormatContactAsHtml(c, ContactDetailsLevel.Minimal))
                    {
                        ParseMode = ParseMode.Html
                    }));
            await botClient.AnswerInlineQueryAsync(inlineQueryId, results, 60);
        }

        public async Task ShowDetails(PersonData person, string[] sources, long chatId)
        {
            var text = new StringBuilder();
            var contact = person.Contact;
            text.AppendLine(
                $@"<b>{contact.LastName} {contact.FirstName} {contact.Patronymic}</b> {contact.FormatMnemonicGroup(DateTime.Now)} (год поступления: {contact.AdmissionYear})");
            text.AppendLine();
            foreach (var rubric in person.Details.GroupBy(d => d.Rubric))
            {
                var sourceId = rubric.First().SourceId;
                var url = sources[sourceId];
                text.AppendLine(
                    $"<b>{EscapeForHtml(rubric.Key)}</b> (<a href=\"{url}\">источник</a>)");
                foreach (var detail in rubric)
                    text.AppendLine($" • {EscapeForHtml(detail.Parameter.TrimEnd('?'))}: {EscapeForHtml(detail.Value)}");
                text.AppendLine();
            }
            await botClient.SendTextMessageAsync(chatId, text.ToString().TrimEnd(), ParseMode.Html);
        }

        public async Task SayReloadStarted(long chatId)
        {
            await botClient.SendTextMessageAsync(chatId, $"Перезагружаю данные из многочисленных гуглтаблиц. Это может занять минуту-другую.", ParseMode.Html);
        }

        public async Task SayReloaded(BotData botData, long chatId)
        {
            var count = botData.AllContacts.Count();
            var studentsCount = botData.Students.Length;
            var teachersCount = botData.Teachers.Length;
            var administratorsCount = botData.Administrators.Length;
            await botClient.SendTextMessageAsync(chatId, $"Загружено {count.Pluralize("контакт|контакта|контактов")}:\n" +
                                                         $"{studentsCount.Pluralize("студент|студента|студентов")}\n" +
                                                         $"{teachersCount.Pluralize("преподаватель|преподавателя|преподавателей")}\n" +
                                                         $"{administratorsCount.Pluralize("администратор|администратора|администраторов")}",
                ParseMode.Html);
        }

        public async Task ShowErrorToDevops(Update incomingUpdate, string errorMessage)
        {
            await botClient.SendTextMessageAsync(settings.DevopsChatId, FormatErrorHtml(incomingUpdate, errorMessage),
                ParseMode.Html);
        }

        public async Task ShowHelp(long chatId, ContactType senderType)
        {
            if (senderType == ContactType.External)
                await SayNoRights(chatId, senderType);
            else
            {
                var b = new StringBuilder("Напиши что-нибудь и я найду кого-нибудь из ФИИТ :)\nЯ понимаю имена, фамилии, юзернеймы Telegram, школы, города, компании и всякое.");
                b.Append("\n\nЕщё можешь прислать мне свою фотографию, и её будут видеть все, кто запросит твой контакт у фиитобота.");
                if (senderType.IsOneOf(ContactType.Administration))
                    b.AppendLine(
                        "\n\nВ любом другом чате напиши @fiitobot и после пробела начни писать фамилию. Я покажу, кого я знаю с такой фамилией, и после выбора конкретного студента, запощу карточку про студента в чат." +
                        $"\n\nДанные я беру из <a href='{settings.SpreadsheetUrl}'>гугл-таблицы к контактами</a>." +
                        $"\nНекоторые фотки я беру из <a href='{settings.PhotoListUrl}'>Яндекс диска</a>." +
                        $"\n\n<b>Секретные админские команды:</b>")
                        .AppendLine("/reload — перезагружает данные из гугл-таблиц.")
                        .AppendLine("/its — обновляет статусы студентов из ИТС УрФУ.")
                        .AppendLine("/tell @user message — отправляет message @user-у от имени фиитобота.")
                        .AppendLine("/as_student ... — как это выглядит для студента.")
                        .AppendLine("/as_staff ... — как это выглядит для препода.")
                        .AppendLine("/as_external ... —  как это выглядит для внешних.");

                await botClient.SendTextMessageAsync(chatId, b.ToString(), ParseMode.Html);
            }
        }

        private string FormatErrorHtml(Update incomingUpdate, string errorMessage)
        {
            var formattedUpdate = FormatIncomingUpdate(incomingUpdate);
            var formattedError = EscapeForHtml(errorMessage);
            return $"Error handling message: {formattedUpdate}\n\nError:\n<pre>{formattedError}</pre>";
        }

        public string FormatIncomingUpdate(Update incomingUpdate)
        {
            var incoming = incomingUpdate.Type switch
            {
                UpdateType.Message => $"From: {incomingUpdate.Message!.From} Message: {incomingUpdate.Message!.Text}",
                UpdateType.EditedMessage =>
                    $"From: {incomingUpdate.EditedMessage!.From} Edit: {incomingUpdate.EditedMessage!.Text}",
                UpdateType.InlineQuery =>
                    $"From: {incomingUpdate.InlineQuery!.From} Query: {incomingUpdate.InlineQuery!.Query}",
                UpdateType.CallbackQuery =>
                    $"From: {incomingUpdate.CallbackQuery!.From} Query: {incomingUpdate.CallbackQuery.Data}",
                _ => $"Message with type {incomingUpdate.Type}"
            };

            return
                $"<pre>{EscapeForHtml(incoming)}</pre>";
        }

        private string EscapeForHtml(string text)
        {
            return text
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;");
        }

        public async Task ShowContact(Contact contact, long chatId, ContactDetailsLevel detailsLevel)
        {
            if (contact.Type == ContactType.Student)
            {
                var inlineKeyboardMarkup = detailsLevel.HasFlag(ContactDetailsLevel.Details)
                    ? new InlineKeyboardMarkup(new InlineKeyboardButton("Подробнее!")
                    { CallbackData = $"/details {contact.LastName} {contact.FirstName}" })
                    : null;
                var htmlText = FormatContactAsHtml(contact, detailsLevel);
                await botClient.SendTextMessageAsync(chatId, htmlText, ParseMode.Html,
                    replyMarkup: inlineKeyboardMarkup);
            }
            else
            {
                var htmlText = FormatContactAsHtml(contact, detailsLevel);
                await botClient.SendTextMessageAsync(chatId, htmlText, ParseMode.Html);
            }
        }

        public async Task ShowPhoto(Contact contact, byte[] photoBytes, long chatId)
        {
            var caption = contact.FirstName + " " + contact.LastName;
            await botClient.SendPhotoAsync(chatId, new InputOnlineFile(new MemoryStream(photoBytes)), caption: caption, parseMode: ParseMode.Html);
        }

        public async Task PromptChangePhoto(long chatId)
        {
            await Say(
                "/changephoto — установит тебе только что загруженную фотографию. " +
                "Когда кто-то запросит твой контакт у фиитобота, он будет показывать эту фотографию. " +
                "Важно, чтобы тебя по ней было легко узнать. " +
                "Это проверяют модераторы фиитобота — плохие фотки они будут отклонять.",
                chatId);
        }

        public async Task OfferToSetHisPhoto(long chatId)
        {
            await Say("Тут могла бы быть твоя фотка, но ее нет. Пришли мне свою фотку, чтобы это исправить!", chatId);
        }

        public async Task ShowPhoto(Contact contact, PersonPhoto photo, long chatId, ContactType senderType)
        {
            var caption = senderType.IsOneOf(ContactType.Administration)
                ? $"<a href='{photo.PhotosDirectory}'>{photo.Name}</a>"
                : contact.FirstName + " " + contact.LastName;
            await botClient.SendPhotoAsync(chatId, new InputOnlineFile(photo.PhotoUri), caption: caption, parseMode: ParseMode.Html);
        }

        public async Task SayNoResults(long chatId)
        {
            var text = "Не нашлось никого подходящего :(\n\nНе унывайте! Найдите кого-нибудь случайного /random! Или поищите по своей школе или городу!";
            await botClient.SendTextMessageAsync(chatId, text, ParseMode.Html);
        }

        public async Task SayBeMoreSpecific(long chatId)
        {
            await botClient.SendTextMessageAsync(chatId, $"Уточните свой запрос", ParseMode.Html);
        }

        public async Task SayNoRights(long chatId, ContactType senderType)
        {
            if (senderType == ContactType.External)
                await botClient.SendTextMessageAsync(chatId, $"Этот бот только для студентов и преподавателей ФИИТ УрФУ. Если вы студент или преподаватель, и вам нужен доступ к контактам студентов, выполните команду /join и модераторы отреагируют на ваш запрос", ParseMode.Html);
            else
                await botClient.SendTextMessageAsync(chatId, "Простите, эта команда не для вас.", ParseMode.Html);
        }

        public string FormatContactAsHtml(Contact contact, ContactDetailsLevel detailsLevel)
        {
            var b = new StringBuilder();
            b.AppendLine($"<b>{contact.LastName} {contact.FirstName} {contact.Patronymic}</b>");
            if (contact.Type == ContactType.Student)
            {
                b.AppendLine($"{contact.FormatMnemonicGroup(DateTime.Now)} (год поступления: {contact.AdmissionYear})");
                if (!string.IsNullOrWhiteSpace(contact.School))
                    b.AppendLine($"🏫 Школа: <code>{contact.School}</code>");
                if (!string.IsNullOrWhiteSpace(contact.City))
                    b.AppendLine($"🏙️ Откуда: <code>{contact.City}</code>");
                if (detailsLevel.HasFlag(ContactDetailsLevel.Marks))
                {
                    b.Append($"Поступление {FormatConcurs(contact.Concurs)}");
                    if (!string.IsNullOrWhiteSpace(contact.EnrollRating))
                        b.Append($" c рейтингом {contact.EnrollRating}");
                }
                if (!string.IsNullOrWhiteSpace(contact.Status) && contact.Status != "Активный")
                    b.AppendLine(contact.Status);
            }
            else if (contact.Type == ContactType.Administration)
            {
                b.AppendLine($"Команда ФИИТ");
                b.AppendLine($"Чем занимается: {contact.FiitJob}");
            }
            else if (contact.Type == ContactType.Staff)
            {
                b.AppendLine($"{contact.FiitJob}");
                if (!string.IsNullOrWhiteSpace(contact.MainCompany))
                    b.AppendLine($"Основное место работы: {contact.MainCompany}");
            }

            b.AppendLine();
            if (detailsLevel.HasFlag(ContactDetailsLevel.Contacts))
            {
                if (!string.IsNullOrWhiteSpace(contact.Email))
                    b.AppendLine($"📧 {contact.Email}");
                if (!string.IsNullOrWhiteSpace(contact.Phone))
                    b.AppendLine($"📞 {contact.Phone}");
            }
            if (!string.IsNullOrWhiteSpace(contact.Telegram))
                b.AppendLine($"💬 {contact.Telegram}");

            b.AppendLine($"{EscapeForHtml(contact.Note)}");

            if (detailsLevel.HasFlag(ContactDetailsLevel.SecretNote) && !string.IsNullOrWhiteSpace(contact.SecretNote))
            {
                b.AppendLine($"{contact.SecretNote}");
            }
            if (detailsLevel.HasFlag(ContactDetailsLevel.Marks))
            {
                b.AppendLine();
                if (contact.CurrentRating.HasValue)
                    b.AppendLine($"Рейтинг: {contact.CurrentRating:0.00}");
            }
            if (detailsLevel.HasFlag(ContactDetailsLevel.TechnicalInfo))
            {
                b.AppendLine();
                b.AppendLine("TelegramId: <code>" + contact.TgId + "</code>");
            }
            if (detailsLevel.HasFlag(ContactDetailsLevel.LinksToFiitTeamFiles))
            {
                b.AppendLine();
                b.AppendLine($"<a href='{settings.SpreadsheetUrl}'>Все контакты ФИИТ</a>");
            }
            return b.ToString();
        }

        private string FormatConcurs(string concurs)
        {
            if (concurs == "О") return "по общему конкурсу";
            else if (concurs == "БЭ") return "по олимпиаде";
            else if (concurs == "К") return "по контракту";
            else if (concurs == "КВ") return "по льготной квоте";
            else if (concurs == "Ц") return "по целевой квоте";
            else return "неизвестно как 🤷‍";
        }

        public async Task ShowContactsBy(string criteria, IList<Contact> people, long chatId)
        {
            people = people.OrderByDescending(p => p.AdmissionYear).ThenBy(p => p.LastName).ThenBy(p => p.FirstName).ToList();
            var listCount = people.Count > 20 ? 15 : people.Count;
            var list = string.Join("\n", people.Select(FormatContactAsListItem).Take(20));
            var ending = listCount < people.Count ? $"\n\nЕсть ещё {people.Count - listCount} подходящих человек" : "";
            if (listCount == 0)
                await botClient.SendTextMessageAsync(chatId, list, ParseMode.Html);
            else
                await botClient.SendTextMessageAsync(chatId, $"{criteria}:\n\n{list}{ending}", ParseMode.Html);
        }

        public async Task ShowDownloadContactsYearSelection(long chatId)
        {
            var inlineKeyboardMarkup = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    new InlineKeyboardButton("2019"){CallbackData = "/contacts_2019"},
                    new InlineKeyboardButton("2020"){CallbackData = "/contacts_2020"},
                    new InlineKeyboardButton("2021"){CallbackData = "/contacts_2021"},
                    new InlineKeyboardButton("2022"){CallbackData = "/contacts_2022"},
                    new InlineKeyboardButton("Все"){CallbackData = "/contacts_all"}
                },
            });
            await botClient.SendTextMessageAsync(
                chatId,
                "Тут можно скачать файл с контактами ФИИТ, подходящий для импорта в Google Contacts (а их телефон может автоматически синхронизировать с контактами Telegram). Выберите год поступления.",
                ParseMode.Html, replyMarkup: inlineKeyboardMarkup);
        }

        public async Task ShowDownloadContactsSuffixSelection(long chatId, string year)
        {
            var inlineKeyboardMarkup = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    new InlineKeyboardButton("Егор фт21 Павлов"){CallbackData = $"/contacts_{year}_ftYY"},
                },
                new[]
                {
                    new InlineKeyboardButton("Егор Владимирович Павлов ФТ21"){CallbackData = $"/contacts_{year}_patronymic"},
                },
                new[]
                {
                    new InlineKeyboardButton("Егор фт Павлов"){CallbackData = $"/contacts_{year}_ft"},
                },
                new[]
                {
                    new InlineKeyboardButton("Егор Павлов"){CallbackData = $"/contacts_{year}_nosuffix"},
                },
            });
            await botClient.SendTextMessageAsync(
                chatId,
                "Можно к имени добавлять пометку ФИИТа или год поступления. Как лучше?",
                ParseMode.Html, replyMarkup: inlineKeyboardMarkup);
        }

        public async Task SendContacts(long chatId, byte[] content, string filename)
        {
            var caption = "Зайдите на https://contacts.google.com и импортируйте этот файл. " +
                          "Если у вас на телефоне контакты синхронизируются с Google, а Telegram синхронизируется с контактами телефона, " +
                          "то через некоторое время контакты в Telegram поменяют имена на правильные.";
            await botClient.SendDocumentAsync(
                chatId,
                new InputOnlineFile(new MemoryStream(content), filename),
                caption: caption);
        }

        public async Task SayUploadPhotoFirst(long chatId)
        {
            var text = "Сначала загрузи свою фотку!";
            await botClient.SendTextMessageAsync(chatId, text, ParseMode.Html);
        }

        public async Task ShowPhotoForModeration(long moderatorChatId, Contact contact, Stream contactNewPhoto)
        {
            await ShowContact(contact, moderatorChatId, ContactDetailsLevel.Minimal);
            await botClient.SendPhotoAsync(moderatorChatId,
                new InputOnlineFile(contactNewPhoto),
                caption: $"{contact.FirstName} {contact.LastName} хочет поменять фотку. Одобряешь?",

                replyMarkup: new InlineKeyboardMarkup(new[]{
                    new InlineKeyboardButton("Одобрить"){CallbackData = "/accept_photo " + contact.TgId},
                    new InlineKeyboardButton("Отклонить"){CallbackData = "/reject_photo " + contact.TgId}
                    }
                    )
                );
        }

        public async Task SayPhotoGoesToModeration(long chatId, Stream photo)
        {
            var text = "Фото ушло на модерацию. Как только его проверят, бот начнет показывать её другим";
            await botClient.SendPhotoAsync(chatId, new InputOnlineFile(photo), caption: text, ParseMode.Html);
        }

        public async Task SayPhotoAccepted(Contact photoOwner, Contact moderator, long chatId)
        {
            var photoOwnerName = photoOwner.TgId == chatId ? "Твоё фото" : $"Фото {photoOwner.FirstName} {photoOwner.LastName}";
            var message = $"{photoOwnerName} приняли!";
            if (moderator != null)
                message += " Модератор " + moderator.FirstLastName();
            await Say(message, chatId);
        }

        public async Task SayPhotoRejected(Contact photoOwner, Contact moderator, long chatId)
        {
            var photoOwnerName = photoOwner.TgId == chatId ? "Твоё фото" : $"Фото {photoOwner.FirstName} {photoOwner.LastName}";
            var message = $"{photoOwnerName} отклонили.";
            if (moderator != null)
                message += " Модератор " + moderator.FirstLastName();
            await Say(message, chatId);
        }

        private static string FormatContactAsListItem(Contact p)
        {
            var who = p.Type switch
            {
                ContactType.Student => p.FormatMnemonicGroup(DateTime.Now),
                ContactType.Administration => "Команда ФИИТ. " + p.FiitJob,
                ContactType.Staff => p.FiitJob,
                _ => p.Type.ToString()
            };
            return $"<code>{p.LastName} {p.FirstName}</code> {p.Telegram} {who}";
        }

        public async Task ShowOtherResults(Contact[] otherContacts, long chatId)
        {
            await ShowContactsBy("Ещё результаты", otherContacts, chatId);
        }

        public async Task Say(string text, long chatId)
        {
            await botClient.SendTextMessageAsync(chatId, text, ParseMode.Html);
        }
    }
}
