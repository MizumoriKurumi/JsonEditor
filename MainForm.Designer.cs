using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

namespace JsonEditor
{
	public partial class MainForm : Form
	{
		private JToken _root;
		private string _currentFilePath;
		private readonly BindingList<FlatProperty> _flatProperties = new BindingList<FlatProperty>();

		private bool _isDirty = false;
		private bool _loading = false;

		private int _dragRowIndex = -1;
		private Point? _dragStartPoint = null;

		private const int _dragScrollMargin = 22;
		private const int _dragScrollSpeed = 3;
		private Timer _dragScrollTimer;
		private int _dragScrollDirection;

		public MainForm()
		{
			InitializeComponent();
			SetupDataGridView();
			SetupLanguageMenu();
			ApplyLanguage();

			_dragScrollTimer = new Timer();
			_dragScrollTimer.Interval = 50;
			_dragScrollTimer.Tick += DragScrollTimer_Tick;

			this.FormClosing += MainForm_FormClosing;
		}

		private void SetupDataGridView()
		{
			dataGridView.AutoGenerateColumns = false;
			dataGridView.AllowUserToAddRows = false;
			dataGridView.AllowUserToDeleteRows = false;
			dataGridView.RowHeadersVisible = true;
			dataGridView.RowHeadersWidth = 50;
			dataGridView.SelectionMode = DataGridViewSelectionMode.CellSelect;
			dataGridView.EditMode = DataGridViewEditMode.EditOnEnter;
			dataGridView.MultiSelect = false;
			dataGridView.AllowDrop = true;

			var pathColumn = new DataGridViewTextBoxColumn
			{
				DataPropertyName = "Path",
				HeaderText = Localization.Get("ColumnKey"),
				Name = "PathColumn",
				ReadOnly = true,
				Width = 300,
				AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
			};
			dataGridView.Columns.Add(pathColumn);

			var valueColumn = new DataGridViewTextBoxColumn
			{
				DataPropertyName = "Value",
				HeaderText = Localization.Get("ColumnValue"),
				Name = "ValueColumn",
				Width = 300,
				AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
			};
			dataGridView.Columns.Add(valueColumn);


			var typeColumn = new DataGridViewComboBoxColumn
			{
				DataPropertyName = "TokenType",
				HeaderText = Localization.Get("ColumnType"),
				Name = "TypeColumn",
				Width = 100,
				Visible = true,
				DataSource = Enum.GetValues(typeof(JTokenType))
								  .Cast<JTokenType>()
								  .Where(t => t == JTokenType.String ||
											  t == JTokenType.Integer ||
											  t == JTokenType.Float ||
											  t == JTokenType.Boolean ||
											  t == JTokenType.Date ||
											  t == JTokenType.Null)
								  .ToList()
			};
			dataGridView.Columns.Add(typeColumn);

			dataGridView.DataSource = _flatProperties;

			dataGridView.CellValidating += DataGridView_CellValidating;
			dataGridView.CellValueChanged += DataGridView_CellValueChanged;
			dataGridView.CellDoubleClick += DataGridView_CellDoubleClick;

			dataGridView.MouseDown += DataGridView_MouseDown;
			dataGridView.MouseMove += DataGridView_MouseMove;
			dataGridView.DragOver += DataGridView_DragOver;
			dataGridView.DragDrop += DataGridView_DragDrop;
		}

		private void DragScrollTimer_Tick(object sender, EventArgs e)
		{
			if (_dragScrollDirection == 0)
				return;


			int index = dataGridView.FirstDisplayedScrollingRowIndex;


			if (_dragScrollDirection < 0)
			{
				if (index > 0)
				{
					dataGridView.FirstDisplayedScrollingRowIndex =
						Math.Max(0, index - 1);
				}
			}
			else
			{
				if (index < dataGridView.RowCount - 1)
				{
					dataGridView.FirstDisplayedScrollingRowIndex =
						Math.Min(dataGridView.RowCount - 1, index + 1);
				}
			}
		}

		private void DataGridView_CellValueChanged(object sender, DataGridViewCellEventArgs e)
		{
			// Header text changes also raise this event, with RowIndex == -1.
			// Only editable data cells represent document changes.
			if (_loading || e.RowIndex < 0 || e.ColumnIndex < 0) return;
			string columnName = dataGridView.Columns[e.ColumnIndex].Name;
			if (columnName != "ValueColumn" && columnName != "TypeColumn") return;
			_isDirty = true;
		}

		private void DataGridView_CellValidating(object sender, DataGridViewCellValidatingEventArgs e)
		{
			if (e.ColumnIndex != 1) return;

			var row = dataGridView.Rows[e.RowIndex];
			var flatProp = row.DataBoundItem as FlatProperty;
			if (flatProp == null) return;

			string newValue = e.FormattedValue?.ToString() ?? string.Empty;
			JTokenType tokenType = flatProp.TokenType;

			if (string.IsNullOrEmpty(newValue))
				return;

			bool valid = true;
			string errorMsg = "";

			switch (tokenType)
			{
				case JTokenType.Integer:
					valid = long.TryParse(newValue, out _);
					if (!valid) errorMsg = Localization.Get("IntegerRequired");
					break;

				case JTokenType.Float:
					valid = double.TryParse(newValue, out _);
					if (!valid) errorMsg = Localization.Get("FloatRequired");
					break;

				case JTokenType.Boolean:
					valid = bool.TryParse(newValue, out _);
					if (!valid) errorMsg = Localization.Get("BooleanRequired");
					break;

				case JTokenType.Date:
					valid = DateTime.TryParse(newValue, out _);
					if (!valid) errorMsg = Localization.Get("DateRequired");
					break;

			}

			if (!valid)
			{
				MessageBox.Show(Localization.Format("InvalidValue", tokenType, errorMsg), Localization.Get("ValidationError"),
								MessageBoxButtons.OK, MessageBoxIcon.Warning);
				e.Cancel = true;
			}
		}

		private void newButton_Click(object sender, EventArgs e)
		{
			if (!CheckAndPromptSave())
				return;

			using (var saveFileDialog = new SaveFileDialog())
			{
				saveFileDialog.Filter = Localization.Get("FileFilter");
				saveFileDialog.Title = Localization.Get("NewFileTitle");
				saveFileDialog.DefaultExt = "json";
				saveFileDialog.AddExtension = true;
				saveFileDialog.FileName = Localization.Get("NewFileName");

				if (saveFileDialog.ShowDialog() != DialogResult.OK)
					return;

				try
				{
					var emptyJson = new JObject();
					string jsonContent = emptyJson.ToString(Newtonsoft.Json.Formatting.Indented);
					File.WriteAllText(saveFileDialog.FileName, jsonContent);

					_root = emptyJson;
					_currentFilePath = saveFileDialog.FileName;
					_isDirty = false;

					RefreshGrid();
					SetStatus("Created", Path.GetFileName(_currentFilePath));
				}
				catch (Exception ex)
				{
					MessageBox.Show(Localization.Format("CreateFailed", ex.Message), Localization.Get("Error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
				}
			}
		}

		private bool CheckAndPromptSave()
		{
			if (!_isDirty)
				return true;

			DialogResult result = MessageBox.Show(
				Localization.Get("UnsavedChanges"),
				Localization.Get("Information"),
				MessageBoxButtons.YesNoCancel,
				MessageBoxIcon.Question);

			if (result == DialogResult.Yes)
			{
				saveButton_Click(this, EventArgs.Empty);
				return !_isDirty;
			}

			if (result == DialogResult.No)
				return true;

			return false;
		}

		private void openButton_Click(object sender, EventArgs e)
		{
			if (!CheckAndPromptSave())
				return;

			using (var openFileDialog = new OpenFileDialog())
			{
				openFileDialog.Filter = Localization.Get("FileFilter");
				openFileDialog.Title = Localization.Get("OpenTitle");

				if (openFileDialog.ShowDialog() != DialogResult.OK)
					return;

				try
				{
					string jsonContent = File.ReadAllText(openFileDialog.FileName);
					_root = JToken.Parse(jsonContent);
					_currentFilePath = openFileDialog.FileName;
					_isDirty = false;

					RefreshGrid();
					SetStatus("Loaded", Path.GetFileName(_currentFilePath));
				}
				catch (Exception ex)
				{
					MessageBox.Show(Localization.Format("LoadFailed", ex.Message), Localization.Get("Error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
				}
			}
		}

		private void saveButton_Click(object sender, EventArgs e)
		{
			if (_root == null)
			{
				MessageBox.Show(Localization.Get("NoFile"), Localization.Get("Information"), MessageBoxButtons.OK, MessageBoxIcon.Information);
				return;
			}

			if (string.IsNullOrEmpty(_currentFilePath))
			{
				SaveAs();
				return;
			}

			try
			{
				CommitGridEdit();

				string json = _root.ToString(Newtonsoft.Json.Formatting.Indented);
				File.WriteAllText(_currentFilePath, json);

				_isDirty = false;
				SetStatus("Saved", Path.GetFileName(_currentFilePath));
				MessageBox.Show(Localization.Get("SaveSucceeded"), Localization.Get("Success"), MessageBoxButtons.OK, MessageBoxIcon.Information);
			}
			catch (Exception ex)
			{
				MessageBox.Show(Localization.Format("SaveFailed", ex.Message), Localization.Get("Error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}

		private void saveAsButton_Click(object sender, EventArgs e)
		{
			if (_root == null)
			{
				MessageBox.Show(Localization.Get("NoFile"), Localization.Get("Information"), MessageBoxButtons.OK, MessageBoxIcon.Information);
				return;
			}

			SaveAs();
		}

		private void SaveAs()
		{
			using (var saveFileDialog = new SaveFileDialog())
			{
				saveFileDialog.Filter = Localization.Get("FileFilter");
				saveFileDialog.Title = Localization.Get("SaveTitle");

				if (!string.IsNullOrEmpty(_currentFilePath))
					saveFileDialog.FileName = Path.GetFileName(_currentFilePath);

				if (saveFileDialog.ShowDialog() != DialogResult.OK)
					return;

				try
				{
					CommitGridEdit();

					string json = _root.ToString(Newtonsoft.Json.Formatting.Indented);
					File.WriteAllText(saveFileDialog.FileName, json);

					_currentFilePath = saveFileDialog.FileName;
					_isDirty = false;
					SetStatus("Saved", Path.GetFileName(_currentFilePath));
					MessageBox.Show(Localization.Get("SaveSucceeded"), Localization.Get("Success"), MessageBoxButtons.OK, MessageBoxIcon.Information);
				}
				catch (Exception ex)
				{
					MessageBox.Show(Localization.Format("SaveFailed", ex.Message), Localization.Get("Error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
				}
			}
		}

		private void addButton_Click(object sender, EventArgs e)
		{
			if (_root == null)
			{
				MessageBox.Show(Localization.Get("NoFile"), Localization.Get("Information"), MessageBoxButtons.OK, MessageBoxIcon.Information);
				return;
			}

			if (_root.Type != JTokenType.Object)
			{
				MessageBox.Show(Localization.Get("RootNotObject"), Localization.Get("Information"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
				return;
			}

			using (var dialog = new AddPropertyDialog())
			{
				if (dialog.ShowDialog() == DialogResult.OK)
				{
					string key = dialog.PropertyName.Trim();
					string value = dialog.PropertyValue.Trim();

					if (string.IsNullOrEmpty(key))
					{
						MessageBox.Show(Localization.Get("EmptyName"), Localization.Get("Error"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
						return;
					}

					try
					{
						JObject obj = (JObject)_root;

						if (obj.Property(key) != null)
						{
							MessageBox.Show(Localization.Get("DuplicateName"), Localization.Get("Error"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
							return;
						}

						JToken newValue;

						if (bool.TryParse(value, out bool b))
							newValue = new JValue(b);
						else if (long.TryParse(value, out long l))
							newValue = new JValue(l);
						else if (double.TryParse(value, out double d))
							newValue = new JValue(d);
						else if (DateTime.TryParse(value, out DateTime dt))
							newValue = new JValue(dt);
						else
							newValue = new JValue(value);

						obj.Add(key, newValue);
						_isDirty = true;
						RefreshGrid();
						SetStatus("Added", key);
					}
					catch (Exception ex)
					{
						MessageBox.Show(Localization.Format("AddFailed", ex.Message), Localization.Get("Error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
					}
				}
			}
		}

		private void DataGridView_MouseDown(object sender, MouseEventArgs e)
		{
			if (e.Button != MouseButtons.Left)
				return;

			var hit = dataGridView.HitTest(e.X, e.Y);

			if (hit.Type == DataGridViewHitTestType.RowHeader && hit.RowIndex >= 0)
			{
				_dragRowIndex = hit.RowIndex;
				_dragStartPoint = Cursor.Position;
			}
			else
			{
				_dragRowIndex = -1;
				_dragStartPoint = null;
			}
		}

		private void DataGridView_MouseMove(object sender, MouseEventArgs e)
		{
			if (_dragRowIndex < 0 || e.Button != MouseButtons.Left)
				return;

			if (!_dragStartPoint.HasValue)
				return;

			int dx = Math.Abs(Cursor.Position.X - _dragStartPoint.Value.X);
			int dy = Math.Abs(Cursor.Position.Y - _dragStartPoint.Value.Y);

			if (dx > SystemInformation.DragSize.Width || dy > SystemInformation.DragSize.Height)
			{
				dataGridView.DoDragDrop(_dragRowIndex, DragDropEffects.Move);
			}
		}

		private void DataGridView_DragOver(object sender, DragEventArgs e)
		{
			if (!e.Data.GetDataPresent(typeof(int)))
			{
				e.Effect = DragDropEffects.None;
				return;
			}


			e.Effect = DragDropEffects.Move;


			Point point = dataGridView.PointToClient(new Point(e.X, e.Y));


			if (point.Y < _dragScrollMargin)
			{
				_dragScrollDirection = -1;
				_dragScrollTimer.Start();
			}
			else if (point.Y > dataGridView.Height - _dragScrollMargin)
			{
				_dragScrollDirection = 1;
				_dragScrollTimer.Start();
			}
			else
			{
				_dragScrollDirection = 0;
				_dragScrollTimer.Stop();
			}
		}

		private void DataGridView_DragDrop(object sender, DragEventArgs e)
		{
			if (!e.Data.GetDataPresent(typeof(int)))
				return;
			_dragScrollDirection = 0;
			_dragScrollTimer.Stop();

			int sourceIndex = (int)e.Data.GetData(typeof(int));

			Point clientPoint = dataGridView.PointToClient(new Point(e.X, e.Y));
			var hit = dataGridView.HitTest(clientPoint.X, clientPoint.Y);
			int targetIndex = hit.RowIndex;

			if (targetIndex < 0 || sourceIndex == targetIndex)
			{
				_dragRowIndex = -1;
				_dragStartPoint = null;
				return;
			}

			MoveFlatProperty(sourceIndex, targetIndex);

			_dragRowIndex = -1;
			_dragStartPoint = null;
		}

		private void MoveFlatProperty(int srcIndex, int dstIndex)
		{
			if (_root == null)
				return;

			if (srcIndex < 0 || dstIndex < 0)
				return;

			if (_flatProperties.Count <= srcIndex || _flatProperties.Count <= dstIndex)
				return;

			try
			{
				CommitGridEdit();

				FlatProperty srcProp = _flatProperties[srcIndex];
				FlatProperty dstProp = _flatProperties[dstIndex];

				string srcPath = srcProp.Path;
				string dstPath = dstProp.Path;

				if (!TryParsePath(srcPath, out string srcParentPath, out string srcKey, out bool srcIsArray) ||
					!TryParsePath(dstPath, out string dstParentPath, out string dstKey, out bool dstIsArray))
				{
					MessageBox.Show(Localization.Get("DragPathInvalid"), Localization.Get("Error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
					return;
				}

				if (srcParentPath != dstParentPath || srcIsArray != dstIsArray)
				{
					MessageBox.Show(Localization.Get("SameParentRequired"), Localization.Get("Information"), MessageBoxButtons.OK, MessageBoxIcon.Information);
					return;
				}

				JToken parent = string.IsNullOrEmpty(srcParentPath)
					? _root
					: _root.SelectToken(srcParentPath);

				if (parent == null)
				{
					MessageBox.Show(Localization.Get("ParentMissing"), Localization.Get("Error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
					return;
				}

				if (srcIsArray)
				{
					JArray array = parent as JArray;

					if (array == null)
						throw new Exception(Localization.Get("ParentNotArray"));

					int srcArrayIndex = int.Parse(srcKey);
					int dstArrayIndex = int.Parse(dstKey);

					if (srcArrayIndex < 0 || srcArrayIndex >= array.Count)
						throw new Exception(Localization.Get("SourceIndexInvalid"));

					if (dstArrayIndex < 0 || dstArrayIndex >= array.Count)
						throw new Exception(Localization.Get("TargetIndexInvalid"));

					MoveJArrayItem(array, srcArrayIndex, dstArrayIndex);
				}
				else
				{
					JObject obj = parent as JObject;

					if (obj == null)
						throw new Exception(Localization.Get("ParentNotObject"));

					MoveJObjectProperty(obj, srcKey, dstKey);
				}

				_isDirty = true;
				RefreshGrid();

				if (dstIndex >= 0 && dstIndex < dataGridView.Rows.Count)
				{
					dataGridView.ClearSelection();
					dataGridView.Rows[dstIndex].Selected = true;
					dataGridView.CurrentCell = dataGridView.Rows[dstIndex].Cells[0];
				}

				SetStatus("Reordered");
			}
			catch (Exception ex)
			{
				MessageBox.Show(Localization.Format("ReorderFailed", ex.Message), Localization.Get("Error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}

		private void MoveJArrayItem(JArray array, int srcIndex, int dstIndex)
		{
			if (srcIndex == dstIndex)
				return;

			JToken item = array[srcIndex];
			item.Remove();
			array.Insert(dstIndex, item);
		}

		private void MoveJObjectProperty(JObject obj, string srcKey, string dstKey)
		{
			if (srcKey == dstKey)
				return;

			List<JProperty> properties = obj.Properties()
				.Select(property => new JProperty(property.Name, property.Value.DeepClone()))
				.ToList();

			int srcIndex = properties.FindIndex(property => property.Name == srcKey);
			int dstIndex = properties.FindIndex(property => property.Name == dstKey);

			if (srcIndex < 0 || dstIndex < 0)
				throw new Exception(Localization.Get("PropertyMissing"));

			JProperty movedProperty = properties[srcIndex];
			properties.RemoveAt(srcIndex);
			properties.Insert(dstIndex, movedProperty);

			obj.RemoveAll();

			foreach (JProperty property in properties)
			{
				obj.Add(property);
			}
		}

		private void DataGridView_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
		{
			if (_root == null)
				return;

			if (e.RowIndex < 0)
				return;

			if (e.ColumnIndex != 0)
				return;

			FlatProperty flatProp = dataGridView.Rows[e.RowIndex].DataBoundItem as FlatProperty;

			if (flatProp == null)
				return;

			try
			{
				CommitGridEdit();

				if (!TryParsePath(flatProp.Path, out string parentPath, out string key, out bool isArray))
				{
					MessageBox.Show(Localization.Get("PropertyPathInvalid"), Localization.Get("Error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
					return;
				}

				if (isArray)
				{
					MessageBox.Show(Localization.Get("ArrayRenameUnsupported"), Localization.Get("Information"), MessageBoxButtons.OK, MessageBoxIcon.Information);
					return;
				}

				JToken parent = string.IsNullOrEmpty(parentPath)
					? _root
					: _root.SelectToken(parentPath);

				JObject obj = parent as JObject;

				if (obj == null)
				{
					MessageBox.Show(Localization.Get("NotObjectProperty"), Localization.Get("Information"), MessageBoxButtons.OK, MessageBoxIcon.Information);
					return;
				}

				JProperty oldProperty = obj.Property(key);

				if (oldProperty == null)
				{
					MessageBox.Show(Localization.Get("RenameTargetMissing"), Localization.Get("Error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
					return;
				}

				using (var dialog = new RenamePropertyDialog(key))
				{
					if (dialog.ShowDialog(this) != DialogResult.OK)
						return;

					string newName = dialog.PropertyName.Trim();

					if (string.IsNullOrEmpty(newName))
					{
						MessageBox.Show(Localization.Get("EmptyName"), Localization.Get("Information"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
						return;
					}

					if (newName == key)
						return;

					if (obj.Property(newName) != null)
					{
						MessageBox.Show(Localization.Get("DuplicateSibling"), Localization.Get("Information"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
						return;
					}

					RenameJObjectProperty(obj, key, newName);

					_isDirty = true;
					RefreshGrid();

					SetStatus("Renamed", key, newName);
				}
			}
			catch (Exception ex)
			{
				MessageBox.Show(Localization.Format("RenameFailed", ex.Message), Localization.Get("Error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}

		private void RenameJObjectProperty(JObject obj, string oldName, string newName)
		{
			bool renamed = false;

			List<JProperty> properties = obj.Properties()
				.Select(property =>
				{
					if (property.Name == oldName)
					{
						renamed = true;
						return new JProperty(newName, property.Value.DeepClone());
					}

					return new JProperty(property.Name, property.Value.DeepClone());
				})
				.ToList();

			if (!renamed)
				throw new Exception(Localization.Get("RenamePropertyMissing"));

			obj.RemoveAll();

			foreach (JProperty property in properties)
			{
				obj.Add(property);
			}
		}

		private bool TryParsePath(string path, out string parentPath, out string key, out bool isArray)
		{
			parentPath = "";
			key = "";
			isArray = false;

			if (string.IsNullOrEmpty(path))
				return false;

			int dotIndex = path.LastIndexOf('.');
			int bracketIndex = path.LastIndexOf('[');

			if (bracketIndex > dotIndex && path.EndsWith("]"))
			{
				isArray = true;
				key = path.Substring(bracketIndex + 1, path.Length - bracketIndex - 2);
				parentPath = bracketIndex > 0 ? path.Substring(0, bracketIndex) : "";
			}
			else if (dotIndex >= 0)
			{
				isArray = false;
				key = path.Substring(dotIndex + 1);
				parentPath = path.Substring(0, dotIndex);
			}
			else
			{
				isArray = false;
				key = path;
				parentPath = "";
			}

			return true;
		}

		private void RefreshGrid()
		{
			_loading = true;

			try
			{
				_flatProperties.Clear();

				if (_root == null)
					return;

				FlattenToken(_root, "", _flatProperties);
			}
			finally
			{
				_loading = false;
			}
		}

		private void FlattenToken(JToken token, string path, ICollection<FlatProperty> result)
		{
			switch (token.Type)
			{
				case JTokenType.Object:
					foreach (var child in ((JObject)token).Properties())
					{
						string childPath = string.IsNullOrEmpty(path) ? child.Name : $"{path}.{child.Name}";
						FlattenToken(child.Value, childPath, result);
					}
					break;

				case JTokenType.Array:
					var array = (JArray)token;

					for (int i = 0; i < array.Count; i++)
					{
						string childPath = $"{path}[{i}]";
						FlattenToken(array[i], childPath, result);
					}
					break;

				default:
					string valueStr = token.Type == JTokenType.Null ? null : token.ToString();

					result.Add(new FlatProperty
					{
						Path = path,
						Value = valueStr,
						TokenType = token.Type,
						OriginalToken = token
					});
					break;
			}
		}

		private void CommitGridEdit()
		{
			if (dataGridView.IsCurrentCellDirty)
				dataGridView.CommitEdit(DataGridViewDataErrorContexts.Commit);

			dataGridView.EndEdit();

			ApplyChangesToToken();
		}

		private void ApplyChangesToToken()
		{
			if (_root == null) return;

			foreach (var prop in _flatProperties)
			{
				JToken targetToken = _root.SelectToken(prop.Path);
				if (targetToken == null) continue;

				string newValue = prop.Value;
				JTokenType targetType = prop.TokenType;
				JToken newTokenValue;

				if (string.IsNullOrEmpty(newValue))
				{
					newTokenValue = targetType == JTokenType.String
									? new JValue(string.Empty)
									: JValue.CreateNull();
				}
				else
				{
					switch (targetType)
					{
						case JTokenType.String:
							newTokenValue = new JValue(newValue);
							break;

						case JTokenType.Integer:

							if (!long.TryParse(newValue, out long longVal))
								throw new FormatException(
									Localization.Format("InvalidInteger", newValue, prop.Path));
							newTokenValue = new JValue(longVal);
							break;

						case JTokenType.Float:

							if (!double.TryParse(newValue, out double doubleVal))
								throw new FormatException(
									Localization.Format("InvalidFloat", newValue, prop.Path));
							newTokenValue = new JValue(doubleVal);
							break;

						case JTokenType.Boolean:
							if (!bool.TryParse(newValue, out bool boolVal))
								throw new FormatException(
									Localization.Format("InvalidBoolean", newValue, prop.Path));
							newTokenValue = new JValue(boolVal);
							break;

						case JTokenType.Date:
							if (!DateTime.TryParse(newValue, out DateTime dtVal))
								throw new FormatException(
									Localization.Format("InvalidDate", newValue, prop.Path));
							newTokenValue = new JValue(dtVal);
							break;

						case JTokenType.Null:
							newTokenValue = string.IsNullOrEmpty(newValue)
											? JValue.CreateNull()
											: new JValue(newValue);
							break;

						default:
							newTokenValue = new JValue(newValue);
							break;
					}
				}

				targetToken.Replace(newTokenValue);
			}
		}

		private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
		{
			if (!CheckAndPromptSave())
				e.Cancel = true;
		}

		private void exitButton_Click(object sender, EventArgs e)
		{
			Close();
		}

		private void InitializeComponent()
		{
			this.toolStrip = new ToolStrip();
			this.newButton = new ToolStripButton();
			this.openButton = new ToolStripButton();
			this.saveButton = new ToolStripButton();
			this.saveAsButton = new ToolStripButton();
			this.toolStripSeparator1 = new ToolStripSeparator();
			this.addButton = new ToolStripButton();
			this.toolStripSeparator2 = new ToolStripSeparator();
			this.exitButton = new ToolStripButton();
			this.statusStrip = new StatusStrip();
			this.statusLabel = new ToolStripStatusLabel();
			this.dataGridView = new DataGridView();

			this.toolStrip.SuspendLayout();
			this.statusStrip.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)(this.dataGridView)).BeginInit();
			this.SuspendLayout();

			this.toolStrip.Items.AddRange(new ToolStripItem[]
			{
				this.newButton,
				this.openButton,
				this.saveButton,
				this.saveAsButton,
				this.toolStripSeparator1,
				this.addButton,
				this.toolStripSeparator2,
				this.exitButton
			});

			this.toolStrip.Location = new Point(0, 0);
			this.toolStrip.Name = "toolStrip";
			this.toolStrip.Size = new Size(900, 25);
			this.toolStrip.TabIndex = 0;

			this.newButton.DisplayStyle = ToolStripItemDisplayStyle.Text;
			this.newButton.Text = Localization.Get("New");
			this.newButton.Click += newButton_Click;

			this.openButton.DisplayStyle = ToolStripItemDisplayStyle.Text;
			this.openButton.Text = Localization.Get("Open");
			this.openButton.Click += openButton_Click;

			this.saveButton.DisplayStyle = ToolStripItemDisplayStyle.Text;
			this.saveButton.Text = Localization.Get("Save");
			this.saveButton.Click += saveButton_Click;

			this.saveAsButton.DisplayStyle = ToolStripItemDisplayStyle.Text;
			this.saveAsButton.Text = Localization.Get("SaveAs");
			this.saveAsButton.Click += saveAsButton_Click;

			this.addButton.DisplayStyle = ToolStripItemDisplayStyle.Text;
			this.addButton.Text = Localization.Get("Add");
			this.addButton.Click += addButton_Click;

			this.exitButton.DisplayStyle = ToolStripItemDisplayStyle.Text;
			this.exitButton.Text = Localization.Get("Exit");
			this.exitButton.Click += exitButton_Click;

			this.statusStrip.Items.AddRange(new ToolStripItem[]
			{
				this.statusLabel
			});

			this.statusStrip.Location = new Point(0, 528);
			this.statusStrip.Name = "statusStrip";
			this.statusStrip.Size = new Size(900, 22);
			this.statusStrip.TabIndex = 1;

			this.statusLabel.Name = "statusLabel";
			this.statusLabel.Size = new Size(0, 17);
			SetStatus("Ready");

			this.dataGridView.Dock = DockStyle.Fill;
			this.dataGridView.Location = new Point(0, 25);
			this.dataGridView.Name = "dataGridView";
			this.dataGridView.Size = new Size(900, 503);
			this.dataGridView.TabIndex = 2;

			this.ClientSize = new Size(900, 550);
			this.MinimumSize = new Size(300, 200);
			this.Controls.Add(this.dataGridView);
			this.Controls.Add(this.statusStrip);
			this.Controls.Add(this.toolStrip);
			this.Name = "MainForm";
			this.Text = Localization.Get("AppTitle");

			this.toolStrip.ResumeLayout(false);
			this.toolStrip.PerformLayout();
			this.statusStrip.ResumeLayout(false);
			this.statusStrip.PerformLayout();
			((System.ComponentModel.ISupportInitialize)(this.dataGridView)).EndInit();
			this.ResumeLayout(false);
			this.PerformLayout();
		}

		private ToolStrip toolStrip;
		private ToolStripButton newButton;
		private ToolStripButton openButton;
		private ToolStripButton saveButton;
		private ToolStripButton saveAsButton;
		private ToolStripSeparator toolStripSeparator1;
		private ToolStripSeparator toolStripSeparator2;
		private ToolStripButton addButton;
		private ToolStripButton exitButton;
		private StatusStrip statusStrip;
		private ToolStripStatusLabel statusLabel;
		private DataGridView dataGridView;
	}

	public class FlatProperty
	{
		public string Path { get; set; }
		public string Value { get; set; }
		public JTokenType TokenType { get; set; }

		[Browsable(false)]
		public JToken OriginalToken { get; set; }
	}

	public class AddPropertyDialog : Form
	{
		private Label lblName;
		private Label lblValue;
		private TextBox txtName;
		private TextBox txtValue;
		private Button btnOk;
		private Button btnCancel;

		public string PropertyName => txtName.Text;
		public string PropertyValue => txtValue.Text;

		public AddPropertyDialog()
		{
			this.Text = Localization.Get("AddTitle");
			this.StartPosition = FormStartPosition.CenterParent;
			this.FormBorderStyle = FormBorderStyle.FixedDialog;
			this.MaximizeBox = false;
			this.MinimizeBox = false;
			this.ClientSize = new Size(300, 150);

			lblName = new Label
			{
				Text = Localization.Get("PropertyName"),
				Location = new Point(15, 15),
				AutoSize = true
			};

			txtName = new TextBox
			{
				Location = new Point(80, 12),
				Width = 190
			};

			lblValue = new Label
			{
				Text = Localization.Get("ValueLabel"),
				Location = new Point(15, 50),
				AutoSize = true
			};

			txtValue = new TextBox
			{
				Location = new Point(80, 47),
				Width = 190
			};

			btnOk = new Button
			{
				Text = Localization.Get("OK"),
				DialogResult = DialogResult.OK,
				Location = new Point(100, 85)
			};

			btnCancel = new Button
			{
				Text = Localization.Get("Cancel"),
				DialogResult = DialogResult.Cancel,
				Location = new Point(190, 85)
			};

			this.AcceptButton = btnOk;
			this.CancelButton = btnCancel;

			this.Controls.AddRange(new Control[]
			{
				lblName,
				txtName,
				lblValue,
				txtValue,
				btnOk,
				btnCancel
			});
		}
	}

	public class RenamePropertyDialog : Form
	{
		private Label lblName;
		private TextBox txtName;
		private Button btnOk;
		private Button btnCancel;

		public string PropertyName => txtName.Text;

		public RenamePropertyDialog(string oldName)
		{
			this.Text = Localization.Get("RenameTitle");
			this.StartPosition = FormStartPosition.CenterParent;
			this.FormBorderStyle = FormBorderStyle.FixedDialog;
			this.MaximizeBox = false;
			this.MinimizeBox = false;
			this.ClientSize = new Size(320, 120);

			lblName = new Label
			{
				Text = Localization.Get("PropertyName"),
				Location = new Point(15, 18),
				AutoSize = true
			};

			txtName = new TextBox
			{
				Location = new Point(80, 15),
				Width = 220,
				Text = oldName
			};

			btnOk = new Button
			{
				Text = Localization.Get("OK"),
				DialogResult = DialogResult.OK,
				Location = new Point(130, 60)
			};

			btnCancel = new Button
			{
				Text = Localization.Get("Cancel"),
				DialogResult = DialogResult.Cancel,
				Location = new Point(220, 60)
			};

			this.AcceptButton = btnOk;
			this.CancelButton = btnCancel;

			this.Controls.AddRange(new Control[]
			{
				lblName,
				txtName,
				btnOk,
				btnCancel
			});

			this.Shown += (sender, args) =>
			{
				txtName.Focus();
				txtName.SelectAll();
			};
		}
	}
}


/*
 * ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
 *           ☆ KURUMI × QIANCHEN ☆
 * ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
 *
 *    ✿ 制作 / Programming
 *       水守胡桃 (Mizumori Kurumi)
 *
 *    ✿ 技术协力 / AI Assistant
 *       芊宸星 (Qianchen Xing)
 *
 *    「星河辽阔，亦有属于我们的微光。」
 *    「果てなき星空にも、私たちだけの小さな光がある。」
 *
 *              ✧ 2026 ✧
 * ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
 */

