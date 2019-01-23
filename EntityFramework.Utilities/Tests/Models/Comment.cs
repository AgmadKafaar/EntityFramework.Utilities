namespace Tests.Models
{
    public class Comment
    {
        public int Id { get; set; }
        public string Text { get; set; }
        public int PostId { get; set; }
        public BlogPost Post { get; set; }
    }
}
