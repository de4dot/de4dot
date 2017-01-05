/*
    Copyright (C) 2011-2015 de4dot@gmail.com

    This file is part of de4dot.

    de4dot is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    de4dot is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with de4dot.  If not, see <http://www.gnu.org/licenses/>.
*/

using System.Collections.Generic;
using de4dot.code.renamer.asmmodules;

namespace de4dot.code.renamer {
	public class MemberInfo {
		protected Ref memberRef;
		public string oldFullName;
		public string oldName;
		public string newName;
		public bool renamed;
		public string suggestedName;

		public MemberInfo(Ref memberRef) {
			this.memberRef = memberRef;
			oldFullName = memberRef.memberRef.FullName;
			oldName = memberRef.memberRef.Name.String;
			newName = memberRef.memberRef.Name.String;
		}

		public void Rename(string newTypeName) {
			renamed = true;
			newName = newTypeName;
		}

		public bool GotNewName() {
			return oldName != newName;
		}

		public override string ToString() {
			return string.Format("O:{0} -- N:{1}", oldFullName, newName);
		}
	}

	public class GenericParamInfo : MemberInfo {
		public GenericParamInfo(MGenericParamDef genericParamDef)
			: base(genericParamDef) {
		}
	}

	public class PropertyInfo : MemberInfo {
		public PropertyInfo(MPropertyDef propertyDef)
			: base(propertyDef) {
		}
	}

	public class EventInfo : MemberInfo {
		public EventInfo(MEventDef eventDef)
			: base(eventDef) {
		}
	}

	public class FieldInfo : MemberInfo {
		public FieldInfo(MFieldDef fieldDef)
			: base(fieldDef) {
		}
	}

	public class MethodInfo : MemberInfo {
		public MMethodDef MethodDef {
			get { return (MMethodDef)memberRef; }
		}

		public MethodInfo(MMethodDef methodDef)
			: base(methodDef) {
		}
	}

	public class ParamInfo {
		//MParamDef paramDef;
		public string oldName;
		public string newName;

		public ParamInfo(MParamDef paramDef) {
			//this.paramDef = paramDef;
			this.oldName = paramDef.ParameterDef.Name;
			this.newName = paramDef.ParameterDef.Name;
		}

		public bool GotNewName() {
			return oldName != newName;
		}
	}

	public class MemberInfos {
		Dictionary<MTypeDef, TypeInfo> allTypeInfos = new Dictionary<MTypeDef, TypeInfo>();
		Dictionary<MPropertyDef, PropertyInfo> allPropertyInfos = new Dictionary<MPropertyDef, PropertyInfo>();
		Dictionary<MEventDef, EventInfo> allEventInfos = new Dictionary<MEventDef, EventInfo>();
		Dictionary<MFieldDef, FieldInfo> allFieldInfos = new Dictionary<MFieldDef, FieldInfo>();
		Dictionary<MMethodDef, MethodInfo> allMethodInfos = new Dictionary<MMethodDef, MethodInfo>();
		Dictionary<MGenericParamDef, GenericParamInfo> allGenericParamInfos = new Dictionary<MGenericParamDef, GenericParamInfo>();
		Dictionary<MParamDef, ParamInfo> allParamInfos = new Dictionary<MParamDef, ParamInfo>();
		DerivedFrom checkWinFormsClass;

		static string[] WINFORMS_CLASSES = new string[] {
#region Win Forms class names
			"System.Windows.Forms.Control",
			"System.Windows.Forms.AxHost",
			"System.Windows.Forms.ButtonBase",
			"System.Windows.Forms.Button",
			"System.Windows.Forms.CheckBox",
			"System.Windows.Forms.RadioButton",
			"System.Windows.Forms.DataGrid",
			"System.Windows.Forms.DataGridView",
			"System.Windows.Forms.DataVisualization.Charting.Chart",
			"System.Windows.Forms.DateTimePicker",
			"System.Windows.Forms.GroupBox",
			"System.Windows.Forms.Integration.ElementHost",
			"System.Windows.Forms.Label",
			"System.Windows.Forms.LinkLabel",
			"System.Windows.Forms.ListControl",
			"System.Windows.Forms.ComboBox",
			"Microsoft.VisualBasic.Compatibility.VB6.DriveListBox",
			"System.Windows.Forms.DataGridViewComboBoxEditingControl",
			"System.Windows.Forms.ListBox",
			"Microsoft.VisualBasic.Compatibility.VB6.DirListBox",
			"Microsoft.VisualBasic.Compatibility.VB6.FileListBox",
			"System.Windows.Forms.CheckedListBox",
			"System.Windows.Forms.ListView",
			"System.Windows.Forms.MdiClient",
			"System.Windows.Forms.MonthCalendar",
			"System.Windows.Forms.PictureBox",
			"System.Windows.Forms.PrintPreviewControl",
			"System.Windows.Forms.ProgressBar",
			"System.Windows.Forms.ScrollableControl",
			"System.Windows.Forms.ContainerControl",
			"System.Windows.Forms.Form",
			"System.ComponentModel.Design.CollectionEditor.CollectionForm",
			"System.Messaging.Design.QueuePathDialog",
			"System.ServiceProcess.Design.ServiceInstallerDialog",
			"System.Web.UI.Design.WebControls.CalendarAutoFormatDialog",
			"System.Web.UI.Design.WebControls.RegexEditorDialog",
			"System.Windows.Forms.Design.ComponentEditorForm",
			"System.Windows.Forms.PrintPreviewDialog",
			"System.Windows.Forms.ThreadExceptionDialog",
			"System.Workflow.Activities.Rules.Design.RuleConditionDialog",
			"System.Workflow.Activities.Rules.Design.RuleSetDialog",
			"System.Workflow.ComponentModel.Design.ThemeConfigurationDialog",
			"System.Workflow.ComponentModel.Design.TypeBrowserDialog",
			"System.Workflow.ComponentModel.Design.WorkflowPageSetupDialog",
			"System.Windows.Forms.PropertyGrid",
			"System.Windows.Forms.SplitContainer",
			"System.Windows.Forms.ToolStripContainer",
			"System.Windows.Forms.ToolStripPanel",
			"System.Windows.Forms.UpDownBase",
			"System.Windows.Forms.DomainUpDown",
			"System.Windows.Forms.NumericUpDown",
			"System.Windows.Forms.UserControl",
			"Microsoft.VisualBasic.Compatibility.VB6.ADODC",
			"System.Web.UI.Design.WebControls.ParameterEditorUserControl",
			"System.Workflow.ComponentModel.Design.WorkflowOutline",
			"System.Workflow.ComponentModel.Design.WorkflowView",
			"System.Windows.Forms.Design.ComponentTray",
			"System.Windows.Forms.Panel",
			"System.Windows.Forms.Design.ComponentEditorPage",
			"System.Windows.Forms.FlowLayoutPanel",
			"System.Windows.Forms.SplitterPanel",
			"System.Windows.Forms.TableLayoutPanel",
			"System.ComponentModel.Design.ByteViewer",
			"System.Windows.Forms.TabPage",
			"System.Windows.Forms.ToolStripContentPanel",
			"System.Windows.Forms.ToolStrip",
			"System.Windows.Forms.BindingNavigator",
			"System.Windows.Forms.MenuStrip",
			"System.Windows.Forms.StatusStrip",
			"System.Windows.Forms.ToolStripDropDown",
			"System.Windows.Forms.ToolStripDropDownMenu",
			"System.Windows.Forms.ContextMenuStrip",
			"System.Windows.Forms.ToolStripOverflow",
			"System.Windows.Forms.ScrollBar",
			"System.Windows.Forms.HScrollBar",
			"System.Windows.Forms.VScrollBar",
			"System.Windows.Forms.Splitter",
			"System.Windows.Forms.StatusBar",
			"System.Windows.Forms.TabControl",
			"System.Windows.Forms.TextBoxBase",
			"System.Windows.Forms.MaskedTextBox",
			"System.Windows.Forms.RichTextBox",
			"System.Windows.Forms.TextBox",
			"System.Windows.Forms.DataGridTextBox",
			"System.Windows.Forms.DataGridViewTextBoxEditingControl",
			"System.Windows.Forms.ToolBar",
			"System.Windows.Forms.TrackBar",
			"System.Windows.Forms.TreeView",
			"System.ComponentModel.Design.ObjectSelectorEditor.Selector",
			"System.Windows.Forms.WebBrowserBase",
			"System.Windows.Forms.WebBrowser",
#endregion
		};

		public MemberInfos() {
			checkWinFormsClass = new DerivedFrom(WINFORMS_CLASSES);
		}

		public bool IsWinFormsClass(MTypeDef type) {
			return checkWinFormsClass.Check(type);
		}

		public TypeInfo Type(MTypeDef t) {
			return allTypeInfos[t];
		}

		public bool TryGetType(MTypeDef t, out TypeInfo info) {
			return allTypeInfos.TryGetValue(t, out info);
		}

		public bool TryGetEvent(MEventDef e, out EventInfo info) {
			return allEventInfos.TryGetValue(e, out info);
		}

		public bool TryGetProperty(MPropertyDef p, out PropertyInfo info) {
			return allPropertyInfos.TryGetValue(p, out info);
		}

		public PropertyInfo Property(MPropertyDef prop) {
			return allPropertyInfos[prop];
		}

		public EventInfo Event(MEventDef evt) {
			return allEventInfos[evt];
		}

		public FieldInfo Field(MFieldDef field) {
			return allFieldInfos[field];
		}

		public MethodInfo Method(MMethodDef method) {
			return allMethodInfos[method];
		}

		public GenericParamInfo GenericParam(MGenericParamDef gparam) {
			return allGenericParamInfos[gparam];
		}

		public ParamInfo Param(MParamDef param) {
			return allParamInfos[param];
		}

		public void Add(MPropertyDef prop) {
			allPropertyInfos[prop] = new PropertyInfo(prop);
		}

		public void Add(MEventDef evt) {
			allEventInfos[evt] = new EventInfo(evt);
		}

		public void Initialize(Modules modules) {
			foreach (var type in modules.AllTypes) {
				allTypeInfos[type] = new TypeInfo(type, this);

				foreach (var gp in type.GenericParams)
					allGenericParamInfos[gp] = new GenericParamInfo(gp);

				foreach (var field in type.AllFields)
					allFieldInfos[field] = new FieldInfo(field);

				foreach (var evt in type.AllEvents)
					Add(evt);

				foreach (var prop in type.AllProperties)
					Add(prop);

				foreach (var method in type.AllMethods) {
					allMethodInfos[method] = new MethodInfo(method);
					foreach (var gp in method.GenericParams)
						allGenericParamInfos[gp] = new GenericParamInfo(gp);
					foreach (var param in method.AllParamDefs)
						allParamInfos[param] = new ParamInfo(param);
				}
			}
		}
	}
}
