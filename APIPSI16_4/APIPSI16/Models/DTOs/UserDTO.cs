namespace APIPSI16.Models.DTOs
{
    public class UserDTO
    {
        public int UserId { get; set; }
        public string? Name { get; set; }
        public string? Email { get; set; }
        public int? Nationality { get; set; }
        public int? JobPreference { get; set; }
        public string? ProfileBio { get; set; }
        public DateOnly? DoB { get; set; }
        public string? PhoneNumber { get; set; }
        public string? ProfilePictureUrl { get; set; }
        public int? Role { get; set; }
        public string? BannerUrl { get; set; }
    }
}