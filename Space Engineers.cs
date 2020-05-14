static class Config
{
	public static char barFull = '■';
	public static char barEmpty = '·';
	public static string barLeft = "[";
	public static string barRight = "]";
	public static char hLine = '─';
}

List<SurfaceProvider> providers;

public Program()
{
	Runtime.UpdateFrequency = UpdateFrequency.Update100;
	providers = new List<SurfaceProvider>();
	List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
	GridTerminalSystem.GetBlocks(blocks);
	List<IMyTerminalBlock> blocksWithInventories = blocks.Where(block => { return block.GetInventory() != null; }).ToList();

	foreach (IMyTerminalBlock block in blocks.Where(block => { return block as IMyTextSurfaceProvider != null && block.CustomData.StartsWith("display"); }))
	{
		SurfaceProvider provider = new SurfaceProvider(block as IMyTextSurfaceProvider);
		providers.Add(provider);

		foreach (string inputLine in block.CustomData.Split(new string[] { "\n" }, StringSplitOptions.RemoveEmptyEntries))
		{
			string[] inputLineComponents = Utils.TrimAll(inputLine.Trim().Split(new string[] { "-" }, StringSplitOptions.RemoveEmptyEntries));
			Dictionary<string, string[]> args = new Dictionary<string, string[]>();

			for (int i = 1; i < inputLineComponents.Length; i++)
			{
				int argEndIndex = inputLineComponents[i].IndexOf(" ");
				string key = inputLineComponents[i].Substring(0, argEndIndex).Trim();
				string[] value = Utils.TrimAll(inputLineComponents[i].Substring(argEndIndex + 1).Trim().Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries));
				args.Add(key, value);
			}

			switch (inputLineComponents[0])
			{
				case "display":
					provider.SetTargetSurface(Int32.Parse(Utils.GetArgValue("id", args, "0")));
					break;

				case "column":
					//provider.SetColumn(Int32.Parse(Utils.GetArgValue("id", args, "0")));
					break;

				case "label":
					provider.AddCommand(new LabelCommand(args));
					break;

				case "inventory":
					provider.AddCommand(new InventoryCommand(args, blocksWithInventories));
					break;

				case "line":
					provider.AddCommand(new LineCommand(args));
					break;

				default:
					break;
			}
		}
	}
}

public void Main(string argument, UpdateType updateSource)
{
	providers.ForEach(provider => { provider.Render(); });
}

class SurfaceProvider
{
	readonly List<Surface> surfaces;
	Surface targetSurface;

	public SurfaceProvider(IMyTextSurfaceProvider textSurfaceProvider)
	{
		surfaces = new List<Surface>();

		for (int i = 0; i < textSurfaceProvider.SurfaceCount; i++)
		{
			surfaces.Add(new Surface(textSurfaceProvider.GetSurface(i)));
		}
	}

	public void SetTargetSurface(int surfaceId)
	{
		targetSurface = surfaces[surfaceId % surfaces.Count];
	}

	public void AddCommand(ICommand command)
	{
		targetSurface.AddCommand(command);
	}

	public void Render()
	{
		surfaces.ForEach(surface => { surface.Render(); });
	}
}

class Surface
{
	readonly int displayWidth;
	readonly List<ICommand> commands;
	readonly MyStringBuilder content;
	readonly IMyTextSurface textSurface;

	public Surface(IMyTextSurface textSurface)
	{
		commands = new List<ICommand>();
		this.textSurface = textSurface;
		displayWidth = (int)((textSurface.TextureSize.X > textSurface.TextureSize.Y ? 50.0f : 25.0f) / textSurface.FontSize);
		this.textSurface.ContentType = ContentType.TEXT_AND_IMAGE;
		content = new MyStringBuilder();
	}

	public void AddCommand(ICommand command)
	{
		command.Configure(displayWidth);
		commands.Add(command);
	}

	public void Render()
	{
		content.Clear();

		commands.ForEach(command =>
		{
			command.Run(content);
		});

		textSurface.WriteText(content.ToString(), false);
	}
}

static class Utils
{
	public static bool Contains(string value, string[] filters)
	{
		foreach (string filter in filters)
		{
			if (value.Contains(filter))
			{
				return true;
			}
		}

		return false;
	}

	public static string[] TrimAll(string[] strings)
	{
		for (int i = 0; i < strings.Length; i++)
		{
			strings[i] = strings[i].Trim();
		}

		return strings;
	}

	public static string AlignCenter(string value, int length)
	{
		return value.PadLeft(((length - value.Length) / 2) + value.Length).PadRight(length);
	}

	public static string AlignRight(string value, int length)
	{
		return String.Format($"{{0,{length}}}", value);
	}

	public static string Align(string value, int length, string alignment)
	{
		switch (alignment)
		{
			case "right": return AlignRight(value, length);
			case "center": return AlignCenter(value, length);
			default: return value;
		}
	}

	public static string CapacityRatio(float current, float max, string label)
	{
		return $"{String.Format("{0:0.0} / {1:0.0}", current, max)} {label}";
	}

	public static string PercentBar(float current, float max, int width)
	{
		string result = Config.barLeft;
		int subWidth = width - 7;
		float ratio = current / max;
		int meterCount = (int)(ratio * subWidth + 0.5f);

		for (int i = 0; i < subWidth; i++)
		{
			result += i < meterCount ? Config.barFull : Config.barEmpty;
		}

		result += $"{Config.barRight}{AlignRight(((int)(ratio * 100 + 0.5f)).ToString(), 5)}%";
		return result;
	}

	public static string[] GetArgValues(string key, Dictionary<string, string[]> args, string defaultValue = "")
	{
		string[] values;

		if (!args.TryGetValue(key, out values))
		{
			values = new string[] { defaultValue };
		}

		return values;
	}

	public static string GetArgValue(string key, Dictionary<string, string[]> args, string defaultValue = "")
	{
		return GetArgValues(key, args, defaultValue)[0];
	}
}

class MyStringBuilder
{
	public int LineCount { get; private set; }
	StringBuilder stringBuilder;

	public MyStringBuilder()
	{
		stringBuilder = new StringBuilder();
	}

	public void Clear()
	{
		LineCount = 0;
		stringBuilder.Clear();
	}

	public void AppendLine(string line)
	{
		LineCount++;
		stringBuilder.AppendLine(line);
	}

	public override string ToString()
	{
		return stringBuilder.ToString();
	}
}

interface ICommand
{
	void Configure(int displayWidth);
	void Run(MyStringBuilder output);
}

abstract class Command : ICommand
{
	protected readonly Dictionary<string, string[]> args;

	public Command(Dictionary<string, string[]> args)
	{
		this.args = args;
	}

	public virtual void Configure(int displayWidth) { }
	public virtual void Run(MyStringBuilder output) { }
}

class LineCommand : Command
{
	string line = "";

	public LineCommand (Dictionary<string, string[]> args) : base(args) { }

	public override void Configure(int displayWidth)
	{
		string width = Utils.GetArgValue("width", args, "0.5");
		float widthRatio = Single.Parse(width);
		line = Utils.AlignCenter(new string(Config.hLine, (int)(displayWidth * widthRatio + 0.5f)), displayWidth);
	}

	public override void Run(MyStringBuilder output)
	{
		output.AppendLine(line);
	}
}

class LabelCommand : Command
{
	string labelText = "";

	public LabelCommand (Dictionary<string, string[]> args) : base(args) { }

	public override void Configure(int displayWidth)
	{
		labelText = Utils.GetArgValue("name", args);
		string align = Utils.GetArgValue("align", args);
		labelText = Utils.Align(labelText, displayWidth, align);
	}

	public override void Run(MyStringBuilder output)
	{
		output.AppendLine(labelText);
	}
}

class InventoryCommand : Command
{
	readonly List<IMyTerminalBlock> filteredInventories;
	string name = "";
	float maxVolume = 0.0f;
	int displayWidth = 1;

	public InventoryCommand (Dictionary<string, string[]> args, List<IMyTerminalBlock> blocksWithInventories) : base(args)
	{
		name = Utils.GetArgValue("name", args);
		string[] blockFilters = Utils.GetArgValues("blocks", args);
		filteredInventories = blocksWithInventories.Where(block => { return Utils.Contains(block.CustomName, blockFilters); }).ToList();
		filteredInventories.ForEach(block => { maxVolume += block.GetInventory().MaxVolume.RawValue; });
		maxVolume /= 1000000.0f;
	}

	public override void Configure(int displayWidth)
	{
		this.displayWidth = displayWidth;
	}

	public override void Run(MyStringBuilder output)
	{
		float currentVolume = 0.0f;
		filteredInventories.ForEach(block => { currentVolume += block.GetInventory().CurrentVolume.RawValue; });
		currentVolume /= 1000000.0f;

		output.AppendLine($"{name} {Utils.AlignRight(Utils.CapacityRatio(currentVolume, maxVolume, "kL"), displayWidth - name.Length)}");
		output.AppendLine(Utils.PercentBar(currentVolume, maxVolume, displayWidth));
	}
}

/*
display -id 0 -cols 2
column -id 0
line -width 0.9
label -name Storage -align center
line -width 1
label
inventory -name Main -blocks Cargo (Storage)
inventory -name Pickup -blocks Cargo (Pickup)
inventory -name Ice -blocks H2

column -id 1
line -width 0.9
label -name Blah2
line -width 1
label -name Blah3
label -name Blah4
label -name Blah5
label -name Blah6
label -name Blah7
label -name Blah8
label -name Blah9
label -name Blah10
label -name Blah11
label -name Blah12
label -name Blah13
*/