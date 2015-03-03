namespace POESKillTree.ViewModels
{
    internal class Attribute
    {
        public string Text { get; set; }
        public float[] Deltas { get; set; }

        public Attribute(string text)
        {
            Text = text;
        }

        public override string ToString()
        {
            return Text;
        }
    }
}