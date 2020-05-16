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
	providers = new List<SurfaceProvider>();
	List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
	GridTerminalSystem.GetBlocks(blocks);
	List<IMyTerminalBlock> blocksWithInventories = blocks.Where(block => { return block.GetInventory() != null; }).ToList();

	foreach (IMyTerminalBlock block in blocks.Where(block => { return block as IMyTextSurfaceProvider != null && (block.CustomData.StartsWith("config") || block.CustomData.StartsWith("panel")); }))
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
				case "config":
					switch (Utils.GetArgValue("update", args, "0"))
					{
						case "1":
							Runtime.UpdateFrequency = UpdateFrequency.Update1;
							break;

						case "10":
							Runtime.UpdateFrequency = UpdateFrequency.Update10;
							break;
						
						default:
							Runtime.UpdateFrequency = UpdateFrequency.Update100;
							break;
					}
					break;

				case "display":
					provider.SetTargetSurface(Int32.Parse(Utils.GetArgValue("id", args, "0")));
					break;

				case "column":
					provider.SetTargetColumn(Int32.Parse(Utils.GetArgValue("id", args, "0")));
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
	providers.ForEach(provider => { provider.Update(); });
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

	public void SetTargetColumn(int columnId)
	{
		targetSurface.SetTargetColumn(columnId);
	}

	public void AddCommand(ICommand command)
	{
		targetSurface.AddCommand(command);
	}

	public void Update()
	{
		surfaces.ForEach(surface => { surface.Update(); });
	}
}

class Surface
{
	readonly int displayWidth;
	readonly List<SurfaceColumn> columns;
	readonly IMyTextSurface textSurface;
	readonly StringBuilder renderBuffer;
	readonly List<MyStringBuilder> columnOutput;
	char[] emptyLine;
	char[] lineRender;
	SurfaceColumn targetColumn;
	int columnWidth;

	public Surface(IMyTextSurface textSurface)
	{
		this.textSurface = textSurface;
		displayWidth = (int)((textSurface.TextureSize.X > textSurface.TextureSize.Y ? 52.0f : 26.0f) / textSurface.FontSize);
		columnWidth = displayWidth;
		this.textSurface.ContentType = ContentType.TEXT_AND_IMAGE;
		columns = new List<SurfaceColumn>();
		columns.Add(new SurfaceColumn(displayWidth));
		renderBuffer = new StringBuilder();
		columnOutput = new List<MyStringBuilder>();
		UpdateLineBuffers();
	}

	public void AddCommand(ICommand command)
	{
		targetColumn.AddCommand(command);
	}

	public void SetTargetColumn(int columnId)
	{
		if (columnId == columns.Count)
		{
			columns.Add(new SurfaceColumn());
			columnWidth = (int)(displayWidth / (float)columns.Count + 0.5f);
			columns.ForEach(column => { column.ChangeWidth(columnWidth - 1); });
			UpdateLineBuffers();
		}

		targetColumn = columns[columnId % columns.Count];
	}

	public void Update()
	{
		renderBuffer.Clear();
		columnOutput.Clear();

		columns.ForEach(column =>
		{
			columnOutput.Add(column.Update());
		});

		for (int line = 0; line < 17; line++)
		{
			for (int col = 0; col < columns.Count; col++)
			{
				if (line < columnOutput[col].LineCount)
				{
					int lineStartIndex = (columnWidth - 1) * line;
					columnOutput[col].StringBuilder.CopyTo(lineStartIndex, lineRender, 0, columnWidth - 1);
					renderBuffer.Append(lineRender);
				}
				else
				{
					renderBuffer.Append(emptyLine);
				}

				renderBuffer.Append(' ');
			}

			renderBuffer.Append('\n');
		}

		textSurface.WriteText(renderBuffer.ToString());
	}

	private void UpdateLineBuffers()
	{
		emptyLine = new char[columnWidth - 1];
		lineRender = new char[columnWidth - 1];

		for (int i = 0; i < columnWidth - 1; i++)
		{
			emptyLine[i] = ' ';
			lineRender[i] = ' ';
		}
	}
}

class SurfaceColumn
{
	readonly List<ICommand> commands;
	readonly MyStringBuilder output;
	int columnWidth;

	public SurfaceColumn(int columnWidth = 0)
	{
		this.columnWidth = columnWidth;
		commands = new List<ICommand>();
		output = new MyStringBuilder();
	}

	public void ChangeWidth(int columnWidth)
	{
		this.columnWidth = columnWidth;
		commands.ForEach(command =>	{ command.Configure(columnWidth); });
	}

	public void AddCommand(ICommand command)
	{
		command.Configure(columnWidth);
		commands.Add(command);
	}

	public MyStringBuilder Update()
	{
		output.Clear();
		commands.ForEach(command => { command.Run(output); } );
		return output;
	}
}

class Utils
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

	public static string AlignLeft(string value, int length)
	{
		return String.Format($"{{0,-{length}}}", value);
	}

	public static string Truncate(string value, int length)
	{
		return value.Substring(0, Math.Min(value.Length, length));
	}

	public static string Align(string value, int length, string alignment)
	{
		switch (alignment)
		{
			case "right": return AlignRight(value, length);
			case "center": return AlignCenter(value, length);
			default: return AlignLeft(value, length);
		}
	}

	public static string CapacityRatio(float current, float max, string label)
	{
		return $"{String.Format("{0:0.0} / {1:0.0}", current, max)} {label}";
	}

	public static string RemainingCapacity(float remaining, string label)
	{
		return $"{String.Format("{0:0.0}", remaining)} {label}";
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

		result += $"{Config.barRight}{AlignRight(((int)(ratio * 100 + 0.5f)).ToString(), 4)}%";
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
	public StringBuilder StringBuilder { get; private set; }

	public MyStringBuilder()
	{
		StringBuilder = new StringBuilder();
	}

	public void Clear()
	{
		LineCount = 0;
		StringBuilder.Clear();
	}

	public void Append(string line)
	{
		LineCount++;
		StringBuilder.Append(line);
	}

	public override string ToString()
	{
		return StringBuilder.ToString();
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
		line = Utils.Truncate(line, displayWidth);
	}

	public override void Run(MyStringBuilder output)
	{
		output.Append(line);
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
		labelText = Utils.Truncate(labelText, displayWidth);
	}

	public override void Run(MyStringBuilder output)
	{
		output.Append(labelText);
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
		string remaining = Utils.RemainingCapacity(maxVolume - currentVolume, "kL");

		output.Append(Utils.Truncate($"{name}{Utils.AlignRight(remaining, displayWidth - name.Length)}", displayWidth));
		output.Append(Utils.Truncate(Utils.PercentBar(currentVolume, maxVolume, displayWidth), displayWidth));
	}
}

/*
display -id 0
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