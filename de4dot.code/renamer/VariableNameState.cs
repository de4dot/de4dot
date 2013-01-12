/*
    Copyright (C) 2011-2013 de4dot@gmail.com

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
	class VariableNameState {
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

		public static VariableNameState create() {
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
		public VariableNameState cloneParamsOnly() {
			var vns = new VariableNameState();
			vns.existingVariableNames = new ExistingNames();
			vns.variableNameCreator = new VariableNameCreator();
			vns.existingVariableNames.merge(existingVariableNames);
			vns.variableNameCreator.merge(variableNameCreator);
			return vns;
		}

		public VariableNameState merge(VariableNameState other) {
			existingVariableNames.merge(other.existingVariableNames);
			existingMethodNames.merge(other.existingMethodNames);
			existingPropertyNames.merge(other.existingPropertyNames);
			existingEventNames.merge(other.existingEventNames);
			variableNameCreator.merge(other.variableNameCreator);
			propertyNameCreator.merge(other.propertyNameCreator);
			eventNameCreator.merge(other.eventNameCreator);
			genericPropertyNameCreator.merge(other.genericPropertyNameCreator);
			staticMethodNameCreator.merge(other.staticMethodNameCreator);
			instanceMethodNameCreator.merge(other.instanceMethodNameCreator);
			return this;
		}

		public void mergeMethods(VariableNameState other) {
			existingMethodNames.merge(other.existingMethodNames);
		}

		public void mergeProperties(VariableNameState other) {
			existingPropertyNames.merge(other.existingPropertyNames);
		}

		public void mergeEvents(VariableNameState other) {
			existingEventNames.merge(other.existingEventNames);
		}

		public string getNewPropertyName(PropertyDef propertyDef) {
			var propType = propertyDef.PropertySig.GetRetType();
			string newName;
			if (isGeneric(propType))
				newName = existingPropertyNames.getName(propertyDef.Name, genericPropertyNameCreator);
			else
				newName = existingPropertyNames.getName(propertyDef.Name, () => propertyNameCreator.create(propType));
			addPropertyName(newName);
			return newName;
		}

		static bool isGeneric(TypeSig type) {
			while (type != null) {
				if (type.IsGenericParameter)
					return true;
				type = type.Next;
			}
			return false;
		}

		public string getNewEventName(EventDef eventDef) {
			string newName = eventNameCreator.create();
			addEventName(newName);
			return newName;
		}

		public void addFieldName(string fieldName) {
			existingVariableNames.add(fieldName);
		}

		public void addParamName(string paramName) {
			existingVariableNames.add(paramName);
		}

		public void addMethodName(string methodName) {
			existingMethodNames.add(methodName);
		}

		public void addPropertyName(string propName) {
			existingPropertyNames.add(propName);
		}

		public void addEventName(string eventName) {
			existingEventNames.add(eventName);
		}

		public bool isMethodNameUsed(string methodName) {
			return existingMethodNames.exists(methodName);
		}

		public bool isPropertyNameUsed(string propName) {
			return existingPropertyNames.exists(propName);
		}

		public bool isEventNameUsed(string eventName) {
			return existingEventNames.exists(eventName);
		}

		public string getNewFieldName(FieldDef field) {
			return existingVariableNames.getName(field.Name, () => variableNameCreator.create(field.FieldSig.GetFieldType()));
		}

		public string getNewFieldName(string oldName, INameCreator nameCreator) {
			return existingVariableNames.getName(oldName, () => nameCreator.create());
		}

		public string getNewParamName(string oldName, Parameter param) {
			return existingVariableNames.getName(oldName, () => variableNameCreator.create(param.Type));
		}

		public string getNewMethodName(string oldName, INameCreator nameCreator) {
			return existingMethodNames.getName(oldName, nameCreator);
		}
	}
}
