using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace ModelCreator.Services
{
    public sealed class SharingInvitee : INotifyPropertyChanged
    {
        private string _email = "";
        private string _role = "Editor";
        public string Email { get { return _email; } set { _email = value; Changed("Email"); } }
        public string Role { get { return _role; } set { _role = value; Changed("Role"); } }
        public event PropertyChangedEventHandler PropertyChanged;
        private void Changed(string property)
        {
            var handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(property));
        }
    }

    internal static class SharingInvitees
    {
        internal static List<SharingInvitee> Validate(IEnumerable<SharingInvitee> entries)
        {
            var source = entries.ToList();
            // Trimble Connect account bulk-update accepts at most 15 users.
            // Validate before creating the Tekla model, not after cloud creation.
            if (source.Count > 15)
                throw new InvalidOperationException(
                    "A maximum of 15 additional users can be included in the initial " +
                    "Trimble Connect invitation. Reduce the list before creating the model.");
            var output = new List<SharingInvitee>();
            var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var roles = new[] { "Owner", "Editor", "Viewer", "Project viewer" };
            foreach (SharingInvitee entry in source)
            {
                string email = (entry.Email ?? "").Trim();
                if (string.IsNullOrEmpty(email))
                    throw new InvalidOperationException("Enter an email address for each additional user, or remove the empty row.");
                if (!email.Contains("@") || email.Contains(" ") || email.StartsWith("@") ||
                    email.EndsWith("@"))
                    throw new InvalidOperationException("Invalid sharing email: " + email);
                if (!unique.Add(email))
                    throw new InvalidOperationException("Duplicate sharing email: " + email);
                if (!roles.Contains(entry.Role))
                    throw new InvalidOperationException("Choose a valid sharing role for " + email);
                output.Add(new SharingInvitee { Email = email, Role = entry.Role });
            }
            return output;
        }
    }
}
