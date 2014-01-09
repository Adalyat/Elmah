#region License, Terms and Author(s)
//
// ELMAH - Error Logging Modules and Handlers for ASP.NET
// Copyright (c) 2004-9 Atif Aziz. All rights reserved.
//
//  Author(s):
//
//      Atif Aziz, http://www.raboof.com
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//    http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
#endregion

[assembly: Elmah.Scc("$Id: DataBinder.cs 623 2009-05-30 00:46:46Z azizatif $")]

namespace Elmah
{
    #region Imports

    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Globalization;
    using System.Linq;
    using System.Reflection;
    using System.Text.RegularExpressions;

    #endregion

    /// <summary>
    /// Provides data expression evaluation facilites similar to 
    /// <see cref="System.Web.UI.DataBinder"/> in ASP.NET.
    /// </summary>

    public static class DataBinder
    {
        public static object Eval(object container, string expression)
        {
            return Eval(container, expression, false);
        }

        public static object Eval(object container, string expression, bool strict)
        {
            if (container == null) throw new ArgumentNullException("container");
            return Compile(expression, strict)(container);
        }

        public static Func<object, object> Compile(string expression)
        {
            return Compile(expression, false);
        }

        public static Func<object, object> Compile(string expression, bool strict)
        {
            return obj => Parse(expression, strict).Aggregate(obj, (node, a) => a(node));
        }

        public static IEnumerable<Func<object, object>> Parse(string expression)
        {
            return Parse(expression, false);
        }

        public static IEnumerable<Func<object, object>> Parse(string expression, bool strict)
        {
            var combinator = strict 
                            ? new Func<Func<object, object>, Func<object, object>>(f => f) 
                            : Optionalize;

            var mp = strict ? (obj, name)  => { throw new Exception(string.Format(@"DataBinding: '{0}' does not contain a property with the name '{1}'.", obj.GetType(), name)); } : (Func<object, string, object>) null;
            var mi = strict ? (obj, index) => { throw new Exception(string.Format(@"DataBinding: '{0}' does not allow indexed access.", obj.GetType())); }                         : (Func<object, object, object>) null;

            return Parse(expression, p => combinator(obj => GetProperty(obj, p, mp)),
                                     i => combinator(obj => GetIndex(obj, i, mi)));
        }

        static Func<object, object> Optionalize(Func<object, object> accessor)
        {
            return obj => obj == null || Convert.IsDBNull(obj) ? null : accessor(obj);
        }

        static object GetProperty(object obj, string name, Func<object, string, object> missingSelector)
        {
            if (obj == null) throw new ArgumentNullException("obj");
            if (name == null) throw new ArgumentNullException("name");
        
            var property = TypeDescriptor.GetProperties(obj).Find(name, true);
            return property != null 
                 ? property.GetValue(obj) 
                 : missingSelector != null 
                 ? missingSelector(obj, name) 
                 : null;
        }

        static object GetIndex(object obj, object index, Func<object, object, object> missingSelector)
        {
            if (obj == null) throw new ArgumentNullException("obj");

            var isIntegralIndex = index is int;
        
            var array = obj as Array;
            if (array != null && isIntegralIndex)
                return array.GetValue((int) index);
        
            var list = obj as IList;
            if (list != null && isIntegralIndex)
                return list[(int) index];
        
            var type = obj.GetType();
            var defaultMember = (DefaultMemberAttribute) Attribute.GetCustomAttribute(type, typeof(DefaultMemberAttribute));
            var defaultMemberName = defaultMember == null || string.IsNullOrEmpty(defaultMember.MemberName) 
                                  ? "Item" 
                                  : defaultMember.MemberName;

            var property = type.GetProperty(defaultMemberName, 
                                            BindingFlags.Public | BindingFlags.Instance, 
                                            null, null, new[] { index.GetType() }, null);

            return property != null 
                 ? property.GetValue(obj, new[] { index })
                 : missingSelector != null 
                 ? missingSelector(obj, index)
                 : null;
        }

        public static IEnumerable<T> Parse<T>(string expression, 
            Func<string, T> propertySelector, 
            Func<object, T> indexSelector)
        {
            if (propertySelector == null) throw new ArgumentNullException("propertySelector");
            if (indexSelector == null) throw new ArgumentNullException("indexSelector");

            expression = (expression ?? string.Empty).Trim();
            if (expression.Length == 0)
                yield break;

            var match = ExpressionRegex.Match(expression);
            if (!match.Success)
                throw new FormatException(null);
        
            var accessors =
                from Capture c in match.Groups["a"].Captures
                select c.Value.TrimStart('.') into text
                let isIndexer = text[0] == '[' || text[0] == '('
                select new { Text = text, IsIndexer = isIndexer };
            
            var indexes =
                from Capture c in match.Groups["i"].Captures
                select c.Value into text
                select text[0] == '\'' || text[0] == '\"'
                     ? (object) text.Substring(1, text.Length - 2)
                     : int.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
            
            using (var i = indexes.GetEnumerator())
            foreach (var a in accessors)
            {
                if (a.IsIndexer)
                    i.MoveNext();
                yield return a.IsIndexer ? indexSelector(i.Current) : propertySelector(a.Text);
            }
        }

        static readonly Regex ExpressionRegex;

        static DataBinder()
        {
            const string index = @"(?<i>[0-9]+ | (?<q>['""]).+?\k<q>)";
            ExpressionRegex = new Regex(@"^(?<a> \.? ( [a-z_][a-z0-9_]* 
                                                     | \[ " + index + @" \]
                                                     | \( " + index + @" \) ) )*$",
                                        RegexOptions.IgnorePatternWhitespace
                                        | RegexOptions.IgnoreCase
                                        | RegexOptions.CultureInvariant);        
        }
    }
}
