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

using dnlib.DotNet;

namespace de4dot.code.renamer {
	public class VariableNameState {
		ExistingNames existingVariableNames;
		ExistingNames existingMethodNames;
		ExistingNames existingPropertyNames;
		ExistingNames existingEventNames;
		TypeNames variableNameCreator;				// For fields and method args
		TypeNames propertyNameCreator;
		NameCreator eventNameCreator;
		NameCreator genericPropertyNameCreator;
		public NameCreator staticMethodNameCreator;
		public NameCreator instanceMethodNameCreator;

		public static VariableNameState Create() {
			var vns = new VariableNameState();
			vns.existingVariableNames = new ExistingNames();
			vns.existingMethodNames = new ExistingNames();
			vns.existingPropertyNames = new ExistingNames();
			vns.existingEventNames = new ExistingNames();
			vns.variableNameCreator = new VariableNameCreator();
			vns.propertyNameCreator = new PropertyNameCreator();
			vns.eventNameCreator = new NameCreator("Event_");
			vns.genericPropertyNameCreator = new NameCreator("Prop_");
			vns.staticMethodNameCreator = new NameCreator("smethod_");
			vns.instanceMethodNameCreator = new NameCreator("method_");
			return vns;
		}

		VariableNameState() {
		}

		// Cloning only params will speed up the method param renaming code
		public VariableNameState CloneParamsOnly() {
			var vns = new VariableNameState();
			vns.existingVariableNames = new ExistingNames();
			vns.variableNameCreator = new VariableNameCreator();
			vns.existingVariableNames.Merge(existingVariableNames);
			vns.variableNameCreator.Merge(variableNameCreator);
			return vns;
		}

		public VariableNameState Merge(VariableNameState other) {
			if (this == other)
				return this;
			existingVariableNames.Merge(other.existingVariableNames);
			existingMethodNames.Merge(other.existingMethodNames);
			existingPropertyNames.Merge(other.existingPropertyNames);
			existingEventNames.Merge(other.existingEventNames);
			variableNameCreator.Merge(other.variableNameCreator);
			propertyNameCreator.Merge(other.propertyNameCreator);
			eventNameCreator.Merge(other.eventNameCreator);
			genericPropertyNameCreator.Merge(other.genericPropertyNameCreator);
			staticMethodNameCreator.Merge(other.staticMethodNameCreator);
			instanceMethodNameCreator.Merge(other.instanceMethodNameCreator);
			return this;
		}

		public void MergeMethods(VariableNameState other) => existingMethodNames.Merge(other.existingMethodNames);
		public void MergeProperties(VariableNameState other) => existingPropertyNames.Merge(other.existingPropertyNames);
		public void MergeEvents(VariableNameState other) => existingEventNames.Merge(other.existingEventNames);

		public string GetNewPropertyName(PropertyDef propertyDef) {
			var propType = propertyDef.PropertySig.GetRetType();
			string newName;
			if (IsGeneric(propType))
				newName = existingPropertyNames.GetName(propertyDef.Name, genericPropertyNameCreator);
			else
				newName = existingPropertyNames.GetName(propertyDef.Name, () => propertyNameCreator.Create(propType));
			AddPropertyName(newName);
			return newName;
		}

		static bool IsGeneric(TypeSig type) {
			while (type != null) {
				if (type.IsGenericParameter)
					return true;
				type = type.Next;
			}
			return false;
		}

		public string GetNewEventName(EventDef eventDef) {
			string newName = eventNameCreator.Create();
			AddEventName(newName);
			return newName;
		}

		public void AddFieldName(string fieldName) => existingVariableNames.Add(fieldName);
		public void AddParamName(string paramName) => existingVariableNames.Add(paramName);
		public void AddMethodName(string methodName) => existingMethodNames.Add(methodName);
		public void AddPropertyName(string propName) => existingPropertyNames.Add(propName);
		public void AddEventName(string eventName) => existingEventNames.Add(eventName);
		public bool IsMethodNameUsed(string methodName) => existingMethodNames.Exists(methodName);
		public bool IsPropertyNameUsed(string propName) => existingPropertyNames.Exists(propName);
		public bool IsEventNameUsed(string eventName) => existingEventNames.Exists(eventName);
		public string GetNewFieldName(FieldDef field) => existingVariableNames.GetName(field.Name, () => variableNameCreator.Create(field.FieldSig.GetFieldType()));
		public string GetNewFieldName(string oldName, INameCreator nameCreator) => existingVariableNames.GetName(oldName, () => nameCreator.Create());
		public string GetNewParamName(string oldName, Parameter param) => existingVariableNames.GetName(oldName, () => variableNameCreator.Create(param.Type));
		public string GetNewMethodName(string oldName, INameCreator nameCreator) => existingMethodNames.GetName(oldName, nameCreator);
	}
}
