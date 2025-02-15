﻿using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace fiitobot
{
    public class BotData
    {
        public Contact[] Administrators;
        public string[] SourceSpreadsheets;
        public PersonData[] Students;
        [JsonIgnore]
        public IEnumerable<PersonData> AllContacts => Students.Concat(Administrators.Select(PersonData.FromContact)).Concat(Teachers?.Select(PersonData.FromContact) ?? Array.Empty<PersonData>());
        public Contact[] Teachers;

        public PersonData[] FindPerson(string query)
        {
            var res = FindPersonIn(query, AllContacts.Where(c => c.Contact.Status == Contact.ActiveStatus));
            if (res.Length == 0)
                res = FindPersonIn(query, AllContacts.Where(c => c.Contact.Status != Contact.ActiveStatus));
            return res;
        }

        private static PersonData[] FindPersonIn(string query, IEnumerable<PersonData> contacts)
        {
            var allSearchable = contacts.ToList();
            var exactResults = allSearchable
                .Where(c => ExactSameContact(c.Contact, query))
                .ToArray();
            if (exactResults.Length > 0)
                return exactResults;
            return allSearchable.Where(c => c.Contact.SameContact(query)).ToArray();
        }

        public PersonData[] SearchPeople(string query)
        {
            var res = FindPerson(query);
            if (res.Length > 0) return res;
            var parts = query.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Select(FindPerson)
                       .Where(g => g.Length > 0)
                       .MinBy(g => g.Length)
                   ?? Array.Empty<PersonData>();
        }

        public static bool ExactSameContact(Contact contact, string query)
        {
            var fullName = contact.LastName + " " + contact.FirstName;
            var tg = contact.Telegram?.TrimStart('@') ?? "";
            var fn = fullName.Canonize();
            return query.Canonize().Equals(fn, StringComparison.InvariantCultureIgnoreCase)
                   || query.Equals(tg, StringComparison.InvariantCultureIgnoreCase)
                   || query.Equals(""+contact.TgId);
        }

        public PersonData FindPersonByTgId(long id)
        {
            return AllContacts.FirstOrDefault(p => p.Contact.TgId == id);
        }

        public PersonData FindPersonByTelegramName(string username)
        {
            return AllContacts.FirstOrDefault(p => p.Contact.SameTelegramUsername(username));
        }
    }

    public class PersonData
    {
        public static PersonData FromContact(Contact contact)
        {
            return new PersonData
            {
                Contact = contact,
                Details = new List<Detail>()
            };
        }
        public Contact Contact;
        public List<Detail> Details;
    }
}
