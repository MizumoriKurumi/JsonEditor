namespace JsonEditor
{
	static class Program
	{
		[STAThread]
		static void Main()
		{
			Localization.Load();
			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(false);
			Application.Run(new MainForm());
		}
	}
}
