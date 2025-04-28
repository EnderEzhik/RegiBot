namespace RegiBot
{
    public enum RegistrationType
    {
        Single,
        Team
    }

    public enum RegistrationStep
    {
        FirstName,
        LastName,
        Age,
        PhoneNumber,
        NextUser
    }

    public class UserData
    {
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public int Age { get; set; }
        public string PhoneNumber { get; set; }
    }

    public class Registration
    {
        public RegistrationType RegistrationType { get; set; }
        public List<UserData> Users { get; set; } = new List<UserData>();
        public RegistrationStep CurrentStep { get; set; } = RegistrationStep.FirstName;
        public int CurrentUserIndex { get; set; } = 0;
    }
}
