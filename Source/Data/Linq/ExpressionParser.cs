﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;

namespace BLToolkit.Data.Linq
{
	using IndexConverter = Func<FieldIndex,FieldIndex>;

	using Data.Sql;
	using DataProvider;
	using Mapping;
	using Reflection;

	class ExpressionParser : ReflectionHelper
	{
		// Should be a single instance.
		//
		static protected readonly ParameterExpression ParametersParam = Expression.Parameter(typeof(object[]), "ps");
	}

	class ExpressionParser<T> : ExpressionParser
	{
		#region Init

		public ExpressionParser()
		{
			_info.Queries.Add(new ExpressionInfo<T>.QueryInfo());
		}

		readonly ExpressionInfo<T>   _info            = new ExpressionInfo<T>();
		readonly ParameterExpression _expressionParam = Expression.Parameter(typeof(Expression),        "expr");
		readonly ParameterExpression _contextParam    = Expression.Parameter(typeof(QueryContext),      "context");
		readonly ParameterExpression _dataReaderParam = Expression.Parameter(typeof(IDataReader),       "rd");
		readonly ParameterExpression _mapSchemaParam  = Expression.Parameter(typeof(MappingSchema),     "ms");
		readonly ParameterExpression _infoParam       = Expression.Parameter(typeof(ExpressionInfo<T>), "info");

		bool   _isSubQueryParsing;
		int    _currentSql = 0;
		Action _buildSelect;
#pragma warning disable 414
		bool   _isParsingPhase;
#pragma warning restore 414

		Func<QuerySource,LambdaInfo,QuerySource> _convertSource = (s,_) => s;

		SqlQuery CurrentSql
		{
			get { return _info.Queries[_currentSql].SqlQuery;  }
			set { _info.Queries[_currentSql].SqlQuery = value; }
		}

		List<ExpressionInfo<T>.Parameter> CurrentSqlParameters
		{
			get { return _info.Queries[_currentSql].Parameters; }
		}

		#endregion

		#region Parsing

		#region Parse

		public ExpressionInfo<T> Parse(
			DataProviderBase      dataProvider,
			MappingSchema         mappingSchema,
			Expression            expression,
			ParameterExpression[] parameters)
		{
			_isParsingPhase = true;

			ParsingTracer.WriteLine(expression);
			ParsingTracer.IncIndentLevel();

			if (parameters != null)
				expression = ConvertParameters(expression, parameters);

			_info.DataProvider  = dataProvider;
			_info.MappingSchema = mappingSchema;
			_info.Expression    = expression;
			_info.Parameters    = parameters;

			ParseInfo.CreateRoot(expression, _expressionParam).Match(
				//
				// db.Select(() => ...)
				//
				pi => pi.IsLambda(0, body => { BuildScalarSelect(body); return true; }),
				//
				// db.Table.ToList()
				//
				pi => pi.IsConstant<IQueryable>((value,_) => BuildSimpleQuery(pi, value.ElementType, null)),
				//
				// from p in db.Table select p
				// db.Table.Select(p => p)
				//
				pi => pi.IsMethod(typeof(Queryable), "Select",
					obj => obj.IsConstant<IQueryable>(),
					arg => arg.IsLambda<T>(
						body => body.NodeType == ExpressionType.Parameter,
						l    => BuildSimpleQuery(pi, typeof(T), l.Expr.Parameters[0].Name))),
				//
				// everything else
				//
				pi =>
				{
					ParsingTracer.WriteLine("Sequence parsing phase...");

					var query = ParseSequence(pi);

					ParsingTracer.WriteLine("Select building phase...");

					_isParsingPhase = false;

					if (_buildSelect != null)
						_buildSelect();
					else
						BuildSelect(query[0], (qs,c) => SetQuery(pi, qs, c), SetQuery, i => i);

					return true;
				}
			);

			ParsingTracer.DecIndentLevel();
			return _info;
		}

		Expression ConvertParameters(Expression expression, ParameterExpression[] parameters)
		{
			return ParseInfo.CreateRoot(expression, _expressionParam).Walk(pi =>
			{
				if (pi.NodeType == ExpressionType.Parameter)
				{
					var idx = Array.IndexOf(parameters, (ParameterExpression)pi.Expr);

					if (idx > 0)
						return pi.Parent.Replace(
							Expression.Convert(
								Expression.ArrayIndex(
									ParametersParam,
									Expression.Constant(Array.IndexOf(parameters, (ParameterExpression)pi.Expr))),
								pi.Expr.Type),
							pi.ParamAccessor);
				}

				return pi;
			});
		}

#if DEBUG && TRACE_PARSING

		QuerySource[] ParseSequence(ParseInfo info)
		{
			ParsingTracer.WriteLine(info);
			ParsingTracer.IncIndentLevel();

			var result = ParseSequenceInternal(info);

			ParsingTracer.DecIndentLevel();
			ParsingTracer.WriteLine(result);

			return result;
		}

		QuerySource[] ParseSequenceInternal(ParseInfo info)

#else
		QuerySource[] ParseSequence(ParseInfo info)

#endif
		{
			var select = ParseTable(info);

			if (select != null)
				return new[] { select };

			switch (info.NodeType)
			{
				case ExpressionType.Call:
					break;

				case ExpressionType.MemberAccess:
					{
						var ma = (MemberExpression)info.Expr;

						if (IsListCountMember(ma.Member))
						{
							var pi = info.ConvertTo<MemberExpression>();
							var ex = pi.Create(ma.Expression, pi.Property(Member.Expression));

							var qs = ParseSequence(ex);

							CurrentSql.Select.Expr(SqlFunction.CreateCount(CurrentSql.From.Tables[0]), "cnt");

							return qs;
						}

						if (TypeHelper.IsSameOrParent(typeof(IQueryable), info.Expr.Type))
						{
							info   = GetIQueriable(info);
							select = ParseTable(info);

							if (select != null)
								return new[] { select };
						}

						var association = GetSource(null, info);

						if (association != null)
						{
							association = _convertSource(association, new LambdaInfo(info));

							if (CurrentSql.From.Tables.Count == 0)
							{
								var table = association as QuerySource.Table;

								if (table != null)
								{
									if (_isSubQueryParsing && _parentQueries.Count > 0)
									{
										foreach (var parentQuery in _parentQueries)
										{
											if (parentQuery.Parent.Find(table.ParentAssociation))
											{
												var query = CreateTable(CurrentSql, table.ObjectType);

												foreach (var cond in table.ParentAssociationJoin.Condition.Conditions)
												{
													var predicate = (SqlQuery.Predicate.ExprExpr)cond.Predicate;
													CurrentSql.Where
														.Expr(predicate.Expr1)
														.Equal
														.Field(query.Columns[((SqlField)predicate.Expr2).Name].Field);
												}

												return new[] { query };
											}
										}
									}

									while (table.ParentAssociation != null)
										table = table.ParentAssociation;

									CurrentSql = table.SqlQuery;
								}
							}

							return new[] { association };
						}
					}

					break;

				case ExpressionType.Parameter:
					if (_parentQueries.Count > 0)
						foreach (var query in _parentQueries)
							if (query.Parameter == info.Expr)
								return new[] { query.Parent };

					goto default;

				default:
					throw new ArgumentException(string.Format("Queryable method call expected. Got '{0}'.", info.Expr), "info");
			}

			QuerySource[] sequence = null;

			info.ConvertTo<MethodCallExpression>().Match
			(
				//
				// db.Table.Method()
				//
				pi => pi.IsQueryableMethod(seq =>
				{
					ParsingTracer.WriteLine(pi);
					ParsingTracer.IncIndentLevel();

					switch (pi.Expr.Method.Name)
					{
						case "Distinct"        : sequence = ParseSequence(seq); select = ParseDistinct(sequence[0]);                 break;
						case "First"           : sequence = ParseSequence(seq); ParseElementOperator(ElementMethod.First);           break;
						case "FirstOrDefault"  : sequence = ParseSequence(seq); ParseElementOperator(ElementMethod.FirstOrDefault);  break;
						case "Single"          : sequence = ParseSequence(seq); ParseElementOperator(ElementMethod.Single);          break;
						case "SingleOrDefault" : sequence = ParseSequence(seq); ParseElementOperator(ElementMethod.SingleOrDefault); break;
						case "Count"           :
						case "Min"             :
						case "Max"             :
						case "Average"         : sequence = ParseSequence(seq); ParseAggregate(pi, null, sequence[0]); break;
						case "OfType"          : sequence = ParseSequence(seq); select = ParseOfType(pi, sequence);    break;
						default                :
							ParsingTracer.DecIndentLevel();
							return false;
					}

					ParsingTracer.DecIndentLevel();
					return true;
				}),
				//
				// db.Table.Method(l => ...)
				//
				pi => pi.IsQueryableMethod((seq,l) =>
				{
					switch (pi.Expr.Method.Name)
					{
						case "Select"            : sequence = ParseSequence(seq); select = ParseSelect    (l, sequence[0]);               break;
						case "Where"             : sequence = ParseSequence(seq); select = ParseWhere     (l, sequence[0]);               break;
						case "SelectMany"        : sequence = ParseSequence(seq); select = ParseSelectMany(l, null, sequence[0]);         break;
						case "OrderBy"           : sequence = ParseSequence(seq); select = ParseOrderBy   (l, sequence[0], false, true);  break;
						case "OrderByDescending" : sequence = ParseSequence(seq); select = ParseOrderBy   (l, sequence[0], false, false); break;
						case "ThenBy"            : sequence = ParseSequence(seq); select = ParseOrderBy   (l, sequence[0], true,  true);  break;
						case "ThenByDescending"  : sequence = ParseSequence(seq); select = ParseOrderBy   (l, sequence[0], true,  false); break;
						case "GroupBy"           : sequence = ParseSequence(seq); select = ParseGroupBy   (l, null, null, sequence[0], pi.Expr.Type.GetGenericArguments()[0]);   break;
						case "First"             : sequence = ParseSequence(seq); select = ParseWhere     (l, sequence[0]); ParseElementOperator(ElementMethod.First);           break;
						case "FirstOrDefault"    : sequence = ParseSequence(seq); select = ParseWhere     (l, sequence[0]); ParseElementOperator(ElementMethod.FirstOrDefault);  break;
						case "Single"            : sequence = ParseSequence(seq); select = ParseWhere     (l, sequence[0]); ParseElementOperator(ElementMethod.Single);          break;
						case "SingleOrDefault"   : sequence = ParseSequence(seq); select = ParseWhere     (l, sequence[0]); ParseElementOperator(ElementMethod.SingleOrDefault); break;
						case "Count"             : sequence = ParseSequence(seq); select = ParseWhere     (l, sequence[0]); ParseAggregate(pi, null, sequence[0]); break;
						case "Min"               :
						case "Max"               :
						case "Average"           : sequence = ParseSequence(seq); ParseAggregate(pi, l, sequence[0]); break;
						default                  : return false;
					}
					return true;
				}),
				//
				// everything else
				//
				pi => pi.IsQueryableMethod ("SelectMany", 1, 2, seq => sequence = ParseSequence(seq), (l1, l2)        => select = ParseSelectMany(l1, l2, sequence[0])),
				pi => pi.IsQueryableMethod ("Join",             seq => sequence = ParseSequence(seq), (i, l2, l3, l4) => select = ParseJoin      (i,  l2, l3, l4, sequence[0])),
				pi => pi.IsQueryableMethod ("GroupJoin",        seq => sequence = ParseSequence(seq), (i, l2, l3, l4) => select = ParseGroupJoin (i,  l2, l3, l4, sequence[0])),
				pi => pi.IsQueryableMethod ("GroupBy",    1, 1, seq => sequence = ParseSequence(seq), (l1, l2)        => select = ParseGroupBy   (l1, l2,   null, sequence[0], pi.Expr.Type.GetGenericArguments()[0])),
				pi => pi.IsQueryableMethod ("GroupBy",    1, 2, seq => sequence = ParseSequence(seq), (l1, l2)        => select = ParseGroupBy   (l1, null, l2,   sequence[0], null)),
				pi => pi.IsQueryableMethod ("GroupBy", 1, 1, 2, seq => sequence = ParseSequence(seq), (l1, l2, l3)    => select = ParseGroupBy   (l1, l2,   l3,   sequence[0], null)),
				pi => pi.IsQueryableMethod ("Take",             seq => sequence = ParseSequence(seq), ex => ParseTake  (sequence[0], ex)),
				pi => pi.IsQueryableMethod ("Skip",             seq => sequence = ParseSequence(seq), ex => ParseSkip  (sequence[0], ex)),
				pi => pi.IsQueryableMethod ("Concat",           seq => sequence = ParseSequence(seq), ex => { select = ParseUnion    (sequence[0], ex, true);  return true; }),
				pi => pi.IsQueryableMethod ("Union",            seq => sequence = ParseSequence(seq), ex => { select = ParseUnion    (sequence[0], ex, false); return true; }),
				pi => pi.IsQueryableMethod ("Except",           seq => sequence = ParseSequence(seq), ex => { select = ParseIntersect(sequence,    ex, true);  return true; }),
				pi => pi.IsQueryableMethod ("Intersect",        seq => sequence = ParseSequence(seq), ex => { select = ParseIntersect(sequence,    ex, false); return true; }),
				pi => pi.IsEnumerableMethod("DefaultIfEmpty",   seq => { select = ParseDefaultIfEmpty(seq); return select != null; }),
				pi => pi.IsMethod(m =>
				{
					if (m.Expr.Method.DeclaringType == typeof(Queryable) || !TypeHelper.IsSameOrParent(typeof(IQueryable), pi.Expr.Type))
						return false;

					sequence = ParseSequence(GetIQueriable(info));
					return true;
				}),
				pi => { throw new ArgumentException(string.Format("Queryable method call expected. Got '{0}'.", pi.Expr), "info"); }
			);

			if (select   == null) return sequence;
			if (sequence == null) return new[] { select };

			var list = new List<QuerySource> { select };

			list.AddRange(sequence);

			return list.ToArray();
		}

		QuerySource ParseTable(ParseInfo info)
		{
			if (info.NodeType == ExpressionType.MemberAccess)
			{
				if (_info.Parameters != null)
				{
					var me = (MemberExpression)info.Expr;

					if (me.Expression == _info.Parameters[0])
						return CreateTable(CurrentSql, new LambdaInfo(info));
				}
			}

			if (info.NodeType == ExpressionType.Call)
			{
				if (_info.Parameters != null)
				{
					var mc = (MethodCallExpression)info.Expr;

					if (mc.Object == _info.Parameters[0])
						return CreateTable(CurrentSql, new LambdaInfo(info));
				}
			}

			QuerySource select = null;

			if (info.IsConstant<IQueryable>((value,expr) =>
			{
				select = CreateTable(CurrentSql, new LambdaInfo(expr));
				return true;
			}))
			{}

			return select;
		}

		ParseInfo GetIQueriable(ParseInfo info)
		{
			if (info.NodeType == ExpressionType.MemberAccess || info.NodeType == ExpressionType.Call)
			{
				var p    = Expression.Parameter(typeof(Expression), "exp");
				var expr = ReplaceParameter(ParseInfo.CreateRoot(info.Expr, p), _ => {});
				var l    = Expression.Lambda<Func<Expression,IQueryable>>(Expression.Convert(expr, typeof(IQueryable)), new [] { p });
				var qe   = l.Compile();
				var n    = _info.AddQueryableAccessors(info.Expr, qe);

				return info.Create(
					qe(info).Expression,
					Expression.Call(
						_infoParam,
						Expressor<ExpressionInfo<T>>.MethodExpressor(a => a.GetIQueryable(0, null)),
						new [] { Expression.Constant(n), info.ParamAccessor }));
			}

			throw new InvalidOperationException();
		}

		#endregion

		#region Parse Select

		QuerySource ParseSelect(LambdaInfo l, params QuerySource[] sources)
		{
			ParsingTracer.WriteLine(l);

#if DEBUG && TRACE_PARSING
			foreach (var source in sources)
				ParsingTracer.WriteLine(source);

			ParsingTracer.IncIndentLevel();

			try
			{
#endif
				for (var i = 0; i < sources.Length && i < l.Parameters.Length; i++)
					SetAlias(sources[i], l.Parameters[i].Expr.Name);

				switch (l.Body.NodeType)
				{
					case ExpressionType.Parameter  :
						for (var i = 0; i < sources.Length; i++)
							if (l.Body == l.Parameters[i].Expr)
								return sources[i];

						foreach (var query in _parentQueries)
							if (l.Body == query.Parameter)
								return query.Parent;

						throw new InvalidOperationException();

					case ExpressionType.MemberAccess:
						{
							var src = GetSource(l, l.Body, sources);

							if (src != null)
							{
								src = _convertSource(src, l);

								if (CurrentSql.From.Tables.Count == 0)
								{
									var table = src as QuerySource.Table;

									if (table != null)
									{
										while (table.ParentAssociation != null)
											table = table.ParentAssociation;

										CurrentSql = table.SqlQuery;
									}
								}

								return src;
							}
						}

						goto default;

					case ExpressionType.New        : return new QuerySource.Expr(CurrentSql, l.ConvertTo<NewExpression>(),        Concat(sources, _parentQueries));
					case ExpressionType.MemberInit : return new QuerySource.Expr(CurrentSql, l.ConvertTo<MemberInitExpression>(), Concat(sources, _parentQueries));
					default                        :
						{
							var scalar = new QuerySource.Scalar(CurrentSql, l, Concat(sources, _parentQueries));
							return scalar.Fields[0] is QuerySource ? (QuerySource)scalar.Fields[0] : scalar;
						}
				}
#if DEBUG && TRACE_PARSING
			}
			finally
			{
				ParsingTracer.DecIndentLevel();
			}
#endif
		}

		#endregion

		#region Parse SelectMany

		QuerySource ParseSelectMany(LambdaInfo collectionSelector, LambdaInfo resultSelector, QuerySource source)
		{
			ParsingTracer.WriteLine();
			ParsingTracer.IncIndentLevel();

			if (collectionSelector.Parameters[0].Expr == collectionSelector.Body.Expr)
			{
				ParsingTracer.DecIndentLevel();
				return resultSelector == null ? source : ParseSelect(resultSelector, source);
			}

			var sql  = CurrentSql;
			var conv = _convertSource;

			CurrentSql = new SqlQuery();

			var associationList = new Dictionary<QuerySource,QuerySource>();

			_convertSource = (s,l) =>
			{
				var t = s as QuerySource.Table;

				if (t != null && t.ParentAssociation != null)
				{
					t.ParentAssociationJoin.IsWeak   = false;
					t.ParentAssociationJoin.JoinType = SqlQuery.JoinType.Inner;

					var orig = s;

					if (t.ParentAssociation != source)
						s = CreateTable(new SqlQuery(), l);

					associationList.Add(s, orig);
				}

				return s;
			};

			_parentQueries.Insert(0, new ParentQuery { Parent = source, Parameter = collectionSelector.Parameters[0] });
			var seq2 = ParseSequence(collectionSelector.Body);
			_parentQueries.RemoveAt(0);

			_convertSource = conv;

			if (associationList.Count > 0)
			{
				foreach (var a in associationList)
				{
					if (a.Key == a.Value)
					{
						CurrentSql = sql;
						break;
					}

					var assc = (QuerySource.Table)a.Key;
					var orig = (QuerySource.Table)a.Value;

					var current = new SqlQuery();
					var source1 = new QuerySource.SubQuery(current, sql,        source,  false);
					var source2 = new QuerySource.SubQuery(current, CurrentSql, seq2[0], false);
					var join    = SqlQuery.InnerJoin(CurrentSql);

					current.From.Table(sql, join);

					CurrentSql = current;

					foreach (var cond in orig.ParentAssociationJoin.Condition.Conditions)
					{
						var ee = (SqlQuery.Predicate.ExprExpr)cond.Predicate;

						var field1 = (SqlField)ee.Expr2;
						var field2 = assc.SqlTable[((SqlField)ee.Expr2).Name];

						var col1 = source1.GetField(field1);
						var col2 = source2.GetField(field2);

						join
							.Expr(col2.GetExpressions(this)[0])
							.Equal
							.Expr(col1.GetExpressions(this)[0]);
					}

					break;
				}

				ParsingTracer.DecIndentLevel();
				return resultSelector == null ? seq2[0] : ParseSelect(resultSelector, source, seq2[0]);
			}

			/*
			if (CurrentSql.From.Tables.Count == 0)
			{
				var table = seq2[0] as QuerySource.Table;

				if (table != null)
				{
					while (table.ParentAssociation != null)
						table = table.ParentAssociation;

					CurrentSql = table.SqlQuery; //.From.Table(table.SqlTable);
				}
			}
			*/

			/*
			if (seq2[0] is QuerySource.Table)
			{
				var tbl = (QuerySource.Table)seq2[0];

				if (tbl.ParentAssociation == source)
				{
					tbl.ParentAssociationJoin.IsWeak   = false;
					tbl.ParentAssociationJoin.JoinType = SqlQuery.JoinType.Inner;

					CurrentSql = sql;
					ParsingTracer.DecIndentLevel();
					return resultSelector == null ? tbl : ParseSelect(resultSelector, source, tbl);
				}

				if (seq2.Length > 1 && seq2[1] is QuerySource.GroupBy)
				{
					var gby = (QuerySource.GroupBy)seq2[1];

					if (tbl.ParentAssociation == gby.OriginalQuery)
					{
						
					}
				}
			}
			*/

			/*
			if (seq2.Length > 1 && seq2[1] is QuerySource.Table)
			{
				var tbl = (QuerySource.Table)seq2[1];

				if (HasSource(seq2[0], tbl.ParentAssociation))
				{
					tbl.ParentAssociationJoin.IsWeak   = false;
					tbl.ParentAssociationJoin.JoinType = SqlQuery.JoinType.Inner;

					if (seq2[0] == tbl.ParentAssociation)
					{
						CurrentSql = sql;
						ParsingTracer.DecIndentLevel();
						return resultSelector == null ? seq2[0] : ParseSelect(resultSelector, source, seq2[0]);
					}
				}
			}
			*/

			if ((source.SqlQuery == seq2[0].SqlQuery && CurrentSql.From.Tables.Count == 0) || source.Sources.Contains(seq2[0]))
			{
				CurrentSql = sql;
				ParsingTracer.DecIndentLevel();
				return resultSelector == null ? seq2[0] : ParseSelect(resultSelector, source, seq2[0]);
			}

			{
				var current = new SqlQuery();
				var source1 = new QuerySource.SubQuery(current, sql,        source);
				var source2 = new QuerySource.SubQuery(current, CurrentSql, seq2[0]);

				//current.From.Table(source1.SubSql);
				//current.From.Table(source2.SubSql);

				CurrentSql = current;

				var result = resultSelector == null ?
					new QuerySource.SubQuery(current, seq2[0].SqlQuery, seq2[0]) :
					ParseSelect(resultSelector, source1, source2);

				ParsingTracer.DecIndentLevel();
				return result;
			}
		}

		static QuerySource.Table GetParentSource(QuerySource parent, QuerySource query)
		{
			if (parent is QuerySource.Table)
			{
				var tbl = (QuerySource.Table)parent;

				foreach (var at in tbl.AssociatedTables.Values)
					if (at == query)
						return tbl;
			}

			foreach (var source in parent.Sources)
			{
				var table = GetParentSource(source, query);
				if (table != null)
					return table;
			}

			return null;
		}

		#endregion

		#region Parse Join

		QuerySource ParseJoin(
			ParseInfo   inner,
			LambdaInfo  outerKeySelector,
			LambdaInfo  innerKeySelector,
			LambdaInfo  resultSelector,
			QuerySource outerSource)
		{
			ParsingTracer.WriteLine();
			ParsingTracer.IncIndentLevel();

			CheckExplicitCtor(outerKeySelector.Body);

			var current = new SqlQuery();
			var source1 = new QuerySource.SubQuery(current, CurrentSql, outerSource);

			CurrentSql = new SqlQuery();

			var seq     = ParseSequence(inner)[0];
			var source2 = new QuerySource.SubQuery(current, CurrentSql, seq, false);
			var join    = source2.SubSql.InnerJoin();

			CurrentSql = current;

			current.From.Table(source1.SubSql, join);

			if (outerKeySelector.Body.NodeType == ExpressionType.New)
			{
				var new1 = outerKeySelector.Body.ConvertTo<NewExpression>();
				var new2 = innerKeySelector.Body.ConvertTo<NewExpression>();

				for (var i = 0; i < new1.Expr.Arguments.Count; i++)
					join
						.Expr(ParseExpression(new1.Create(new1.Expr.Arguments[i], new1.Index(new1.Expr.Arguments, New.Arguments, i)), source1)).Equal
						.Expr(ParseExpression(new2.Create(new2.Expr.Arguments[i], new2.Index(new2.Expr.Arguments, New.Arguments, i)), source2));
			}
			else
			{
				join
					.Expr(ParseExpression(outerKeySelector.Body, source1)).Equal
					.Expr(ParseExpression(innerKeySelector.Body, source2));
			}

			var result = resultSelector == null ? source2 : ParseSelect(resultSelector, source1, source2);
			ParsingTracer.DecIndentLevel();
			return result;
		}

		static void CheckExplicitCtor(Expression expr)
		{
			if (expr.NodeType == ExpressionType	.MemberInit)
				throw new NotSupportedException(
					string.Format("Explicit construction of entity type '{0}' in query is not allowed.", expr.Type));
		}

		#endregion

		#region Parse GroupJoin

		QuerySource ParseGroupJoin(
			ParseInfo   inner,
			LambdaInfo  outerKeySelector,
			LambdaInfo  innerKeySelector,
			LambdaInfo  resultSelector,
			QuerySource outerSource)
		{
			ParsingTracer.WriteLine();
			ParsingTracer.IncIndentLevel();

			if (outerKeySelector.Body.NodeType == ExpressionType.MemberInit)
				throw new NotSupportedException(
					string.Format("Explicit construction of entity type '{0}' in query is not allowed.",
					outerKeySelector.Body.Expr.Type));

			// Process outer source.
			//
			var current = new SqlQuery();
			var source1 = new QuerySource.SubQuery(current, CurrentSql, outerSource);

			// Process inner source.
			//
			CurrentSql = new SqlQuery();

			var seq     = ParseSequence(inner)[0];
			var source2 = new QuerySource.GroupJoin(current, CurrentSql, seq);
			var join    = source2.SubSql.LeftJoin();

			CurrentSql = current;

			current.From.Table(source1.SubSql, join);

			// Process counter.
			//
			CurrentSql = new SqlQuery();

			var cntseq   = ParseSequence(inner)[0];
			var counter  = new QuerySource.SubQuery(current, CurrentSql, cntseq, false);
			var cntjoin  = counter.SubSql.WeakLeftJoin();
 
			CurrentSql = current;

			counter.SubSql.Select.Expr(SqlFunction.CreateCount(counter.SubSql.From.Tables[0]), "cnt");
			current.From.Table(source1.SubSql, cntjoin);

			// Make join and where for the counter.
			//
			if (outerKeySelector.Body.NodeType == ExpressionType.New)
			{
				var new1 = outerKeySelector.Body.ConvertTo<NewExpression>();
				var new2 = innerKeySelector.Body.ConvertTo<NewExpression>();

				for (var i = 0; i < new1.Expr.Arguments.Count; i++)
				{
					join
						.Expr(ParseExpression(new1.Create(new1.Expr.Arguments[i], new1.Index(new1.Expr.Arguments, New.Arguments, i)), source1)).Equal
						.Expr(ParseExpression(new2.Create(new2.Expr.Arguments[i], new2.Index(new2.Expr.Arguments, New.Arguments, i)), source2));

					//counter.SqlQuery.Where
					cntjoin
						.Expr(ParseExpression(new1.Create(new1.Expr.Arguments[i], new1.Index(new1.Expr.Arguments, New.Arguments, i)), source1)).Equal
						.Expr(ParseExpression(new2.Create(new2.Expr.Arguments[i], new2.Index(new2.Expr.Arguments, New.Arguments, i)), counter));

					counter.SubSql.GroupBy
						.Expr(ParseExpression(new2.Create(new2.Expr.Arguments[i], new2.Index(new2.Expr.Arguments, New.Arguments, i)), cntseq));
				}
			}
			else
			{
				join
					.Expr(ParseExpression(outerKeySelector.Body, source1)).Equal
					.Expr(ParseExpression(innerKeySelector.Body, source2));

				cntjoin
					.Expr(ParseExpression(outerKeySelector.Body, source1)).Equal
					.Expr(ParseExpression(innerKeySelector.Body, counter));

				counter.SubSql.GroupBy
					.Expr(ParseExpression(innerKeySelector.Body, cntseq));
			}

			if (resultSelector == null)
				return source2;
			
			var select = ParseSelect(resultSelector, source1, source2, counter);

			source2.Counter = new QueryField.ExprColumn(select, counter.SubSql.Select.Columns[0], null);
			//source2.SourceInfo = inner;

			ParsingTracer.DecIndentLevel();
			return select;
		}

		#endregion

		#region Parse DefaultIfEmpty

		QuerySource ParseDefaultIfEmpty(ParseInfo seq)
		{
			return GetField(seq) as QuerySource;
		}

		#endregion

		#region Parse OfType

		QuerySource ParseOfType(ParseInfo pi, QuerySource[] sequence)
		{
			var table = sequence[0] as QuerySource.Table;

			if (table != null && table.InheritanceMapping.Count > 0)
			{
				var objectType = pi.Expr.Type.GetGenericArguments()[0];

				if (TypeHelper.IsSameOrParent(table.ObjectType, objectType))
				{
					var predicate = MakeIsPredicate(table, objectType);

					if (predicate.GetType() != typeof(SqlQuery.Predicate.Expr))
						CurrentSql.Where.SearchCondition.Conditions.Add(new SqlQuery.Condition(false, predicate));
				}
			}

			return sequence[0];
		}

		#endregion

		#region Parse Where

		QuerySource ParseWhere(LambdaInfo l, QuerySource select)
		{
			ParsingTracer.WriteLine(l);
			ParsingTracer.WriteLine(select);
			ParsingTracer.IncIndentLevel();

			SetAlias(select, l.Parameters[0].Expr.Name);

			bool makeHaving;

			if (CheckSubQueryForWhere(select, l.Body, out makeHaving))
				select = WrapInSubQuery(select);

			ParseSearchCondition(
				makeHaving? CurrentSql.Having.SearchCondition.Conditions : CurrentSql.Where.SearchCondition.Conditions,
				l, l.Body, select);

			ParsingTracer.DecIndentLevel();
			return select;
		}

		bool CheckSubQueryForWhere(QuerySource query, ParseInfo expr, out bool makeHaving)
		{
			var checkParameter = query is QuerySource.Scalar && query.Fields[0] is QueryField.ExprColumn;
			var makeSubQuery   = false;
			var isHaving       = false;
			var isWhere        = false;

			expr.Walk(pi =>
			{
				if (IsSubQuery(pi, query))
				{
					pi.StopWalking = isWhere = true;
					return pi;
				}

				switch (pi.NodeType)
				{
					case ExpressionType.MemberAccess:
						{
							var ma      = (MemberExpression)pi.Expr;
							var isCount = IsListCountMember(ma.Member);

							if (!IsNullableValueMember(ma.Member) && !isCount)
							{
								if (_info.SqlProvider.ConvertMember(ma.Member) == null)
								{
									var field = GetField(pi, query);

									if (field is QueryField.ExprColumn)
										makeSubQuery = pi.StopWalking = true;
								}
							}

							if (isCount)
							{
								isHaving = true;
								pi.StopWalking = true;
							}
							else
								isWhere = true;

							break;
						}

					case ExpressionType.Call:
						{
							var e = pi.Expr as MethodCallExpression;

							if (e.Method.DeclaringType == typeof(Enumerable) && e.Method.Name != "Contains")
							{
								isHaving = true;
								pi.StopWalking = true;
							}
							else
								isWhere = true;

							break;
						}

					case ExpressionType.Parameter:
						if (checkParameter)
							makeSubQuery = pi.StopWalking = true;

						isWhere = true;

						break;
				}

				return pi;
			});

			makeHaving = isHaving && !isWhere;
			return makeSubQuery || isHaving && isWhere;
		}

		#endregion

		#region Parse GroupBy

		QuerySource ParseGroupBy(LambdaInfo keySelector, LambdaInfo elementSelector, LambdaInfo resultSelector, QuerySource source, Type groupingType)
		{
			ParsingTracer.WriteLine();
			ParsingTracer.IncIndentLevel();

			CheckExplicitCtor(keySelector.Body);

			var group   = ParseSelect(keySelector, source);
			var element = elementSelector != null? ParseSelect(elementSelector, source) : null;
			var fields  = new List<QueryField>(group is QuerySource.Table ? group.GetKeyFields() : group.Fields);
			var byExprs = new ISqlExpression[fields.Count];
			var wrap    = false;

			for (var i = 0; i < fields.Count; i++)
			{
				var field = fields[i];
				var exprs = field.GetExpressions(this);

				if (exprs == null || exprs.Length != 1)
					throw new LinqException("Cannot group by type '{0}'", keySelector.Body.Expr.Type);

				byExprs[i] = exprs[0];

				wrap = wrap || !(exprs[0] is SqlField || exprs[0] is SqlQuery.Column);
			}

			// Can be used instead of GroupBy.Items.Clear().
			//
			//if (!wrap)
			//	wrap = CurrentSql.GroupBy.Items.Count > 0;

			if (wrap)
			{
				var subQuery = WrapInSubQuery(group);

				foreach (var field in fields)
					CurrentSql.GroupBy.Expr(group.SqlQuery.Select.Columns[field.Select(this)[0].Index]);

				group = subQuery;
			}
			else
			{
				CurrentSql.GroupBy.Items.Clear();
				foreach (var field in byExprs)
					CurrentSql.GroupBy.Expr(field);
			}

			var result =
				resultSelector == null ?
					new QuerySource.GroupBy(CurrentSql, group, source, keySelector, element, groupingType, wrap, byExprs) :
					ParseSelect(resultSelector, group, source);

			ParsingTracer.DecIndentLevel();
			return result;
		}

		#endregion

		#region Parse OrderBy

		QuerySource ParseOrderBy(LambdaInfo lambda, QuerySource source, bool isThen, bool ascending)
		{
			ParsingTracer.WriteLine();
			ParsingTracer.IncIndentLevel();

			CheckExplicitCtor(lambda.Body);

			if (CurrentSql.Select.TakeValue != null || CurrentSql.Select.SkipValue != null)
				source = WrapInSubQuery(source);

			var order  = ParseSelect(lambda, source);
			var fields = new List<QueryField>(order is QuerySource.Table ? order.GetKeyFields().Select(f => f) : order.Fields);

			if (!isThen)
				CurrentSql.OrderBy.Items.Clear();

			foreach (var field in fields)
			{
				var exprs = field.GetExpressions(this);

				if (exprs == null)
					throw new LinqException("Cannot order by type '{0}'", lambda.Body.Expr.Type);

				foreach (var expr in exprs)
				{
					var e = expr;

					if (e is SqlQuery.SearchCondition)
					{
						if (e.CanBeNull())
						{
							var notExpr = new SqlQuery.SearchCondition
							{
								Conditions = { new SqlQuery.Condition(true, new SqlQuery.Predicate.Expr(expr, expr.Precedence)) }
							};

							e = Convert(new SqlFunction("CASE", expr, new SqlValue(1), notExpr, new SqlValue(0), new SqlValue(null)));
						}
						else
							e = Convert(new SqlFunction("CASE", expr, new SqlValue(1), new SqlValue(0)));
					}

					CurrentSql.OrderBy.Expr(e, !ascending);
				}
			}

			ParsingTracer.DecIndentLevel();
			return source;
		}

		#endregion

		#region Parse Take

		bool ParseTake(QuerySource select, ParseInfo value)
		{
			if (value.Expr.Type != typeof(int))
				return false;

			ParsingTracer.WriteLine();
			ParsingTracer.IncIndentLevel();

			CurrentSql.Select.Take(ParseExpression(value, select));

			_info.SqlProvider.SqlQuery = CurrentSql;

			if (CurrentSql.Select.SkipValue != null && _info.SqlProvider.IsTakeSupported && !_info.SqlProvider.IsSkipSupported)
				CurrentSql.Select.Take(Convert(
					new SqlBinaryExpression(CurrentSql.Select.SkipValue, "+", CurrentSql.Select.TakeValue, typeof(int), Precedence.Additive)));

			if (!_info.SqlProvider.TakeAcceptsParameter)
			{
				var p = CurrentSql.Select.TakeValue as SqlParameter;

				if (p != null)
					p.IsQueryParameter = false;
			}

			ParsingTracer.DecIndentLevel();
			return true;
		}

		#endregion

		#region Parse Skip

		bool ParseSkip(QuerySource select, ParseInfo value)
		{
			if (value.Expr.Type != typeof(int))
				return false;

			ParsingTracer.WriteLine();
			ParsingTracer.IncIndentLevel();

			var prevSkipValue = CurrentSql.Select.SkipValue;

			CurrentSql.Select.Skip(ParseExpression(value, select));

			_info.SqlProvider.SqlQuery = CurrentSql;

			if (CurrentSql.Select.TakeValue != null)
			{
				if (_info.SqlProvider.IsSkipSupported || !_info.SqlProvider.IsTakeSupported)
					CurrentSql.Select.Take(Convert(
						new SqlBinaryExpression(CurrentSql.Select.TakeValue, "-", CurrentSql.Select.SkipValue, typeof (int), Precedence.Additive)));

				if (prevSkipValue != null)
					CurrentSql.Select.Skip(Convert(
						new SqlBinaryExpression(prevSkipValue, "+", CurrentSql.Select.SkipValue, typeof (int), Precedence.Additive)));
			}

			if (!_info.SqlProvider.TakeAcceptsParameter)
			{
				var p = CurrentSql.Select.SkipValue as SqlParameter;

				if (p != null)
					p.IsQueryParameter = false;
			}

			ParsingTracer.DecIndentLevel();
			return true;
		}

		#endregion

		#region ParseDistinct

		QuerySource ParseDistinct(QuerySource select)
		{
			ParsingTracer.WriteLine(select);
			ParsingTracer.IncIndentLevel();

			if (CurrentSql.Select.TakeValue != null || CurrentSql.Select.SkipValue != null)
				select = WrapInSubQuery(select);

			CurrentSql.Select.IsDistinct = true;

			ParsingTracer.DecIndentLevel();
			return select;
		}

		#endregion

		#region Parse Aggregate

		interface IAggregateHelper
		{
			void SetAggregate(ExpressionParser<T> parser, ParseInfo pi);
		}

		class AggregateHelper<TE> : IAggregateHelper
		{
			public void SetAggregate(ExpressionParser<T> parser, ParseInfo pi)
			{
				var mapper = Expression.Lambda<ExpressionInfo<T>.Mapper<TE>>(
					pi, new[]
					{
						parser._infoParam,
						parser._contextParam,
						parser._dataReaderParam,
						parser._mapSchemaParam,
						parser._expressionParam,
						ParametersParam
					});

				parser._info.SetElementQuery(mapper.Compile());
			}
		}

		void ParseAggregate(ParseInfo<MethodCallExpression> parseInfo, LambdaInfo lambda, QuerySource select)
		{
			ParsingTracer.WriteLine(parseInfo);
			ParsingTracer.WriteLine(lambda);
			ParsingTracer.WriteLine(select);
			ParsingTracer.IncIndentLevel();

			var query = select;

			if (query.SqlQuery.Select.IsDistinct)
			{
				query.Select(this);
				query = WrapInSubQuery(query);
			}

			var name = parseInfo.Expr.Method.Name;

			if (lambda == null && name != "Count" && query.Fields.Count != 1)
				throw new LinqException("Incorrent use of the '{0}' function.", name);

			var sql = query.SqlQuery;
			var idx =
				name == "Count" ?
					sql.Select.Add(SqlFunction.CreateCount(sql), "cnt") :
					lambda != null ?
						sql.Select.Add(new SqlFunction(name, ParseExpression(lambda.Body, query))) :
						sql.Select.Add(new SqlFunction(name, query.Fields[0].GetExpressions(this)[0]));

			if (!_isSubQueryParsing)
				_buildSelect = () =>
				{
					var pi     = BuildField(parseInfo, new[] { idx });
					var helper = (IAggregateHelper)Activator.CreateInstance(typeof(AggregateHelper<>).MakeGenericType(typeof(T), parseInfo.Expr.Type));

					helper.SetAggregate(this, pi);
				};

			ParsingTracer.DecIndentLevel();
		}

		#endregion

		#region ParseElementOperator

		void ParseElementOperator(ElementMethod elementMethod)
		{
			var take = 0;

			if (_parentQueries.Count == 0 || _info.SqlProvider.IsSubQueryTakeSupported)
				switch (elementMethod)
				{
					case ElementMethod.First           :
					case ElementMethod.FirstOrDefault  :
						take = 1;
						break;

					case ElementMethod.Single          :
					case ElementMethod.SingleOrDefault :
						if (_parentQueries.Count == 0)
							take = 2;
						break;
				}

			if (take != 0)
				CurrentSql.Select.Take(take);

			_info.MakeElementOperator(elementMethod);
		}

		#endregion

		#region ParseUnion

		QuerySource ParseUnion(QuerySource select, ParseInfo ex, bool all)
		{
			var sql = CurrentSql;

			CurrentSql = new SqlQuery();

			var query = ParseSequence(ex);
			var union = new SqlQuery.Union(query[0].SqlQuery, all);

			CurrentSql = sql;

			var sq = select as QuerySource.SubQuery;

			if (sq == null || !sq.SubSql.HasUnion || !sql.IsSimple)
			{
				sq = WrapInSubQuery(select);
			}

			sq.SubSql.Unions.Add(union);
			sq.Unions.Add(query[0]);

			return sq;
		}

		#endregion

		#region ParseIntersect

		QuerySource ParseIntersect(QuerySource[] queries, ParseInfo expr, bool isNot)
		{
			var select = WrapInSubQuery(queries[0]);
			var sql    = CurrentSql;

			CurrentSql = new SqlQuery();

			var query  = ParseSequence(expr)[0];
			var except = CurrentSql;

			except.ParentSql = sql;

			CurrentSql = sql;

			if (isNot)
				sql.Where.Not.Exists(except);
			else
				sql.Where.Exists(except);

			var keys1 = select.GetKeyFields();
			var keys2 = query. GetKeyFields();

			if (keys1 == null || keys1.Count == 0 || keys1.Count != keys2.Count)
				throw new InvalidOperationException();

			for (var i = 0; i < keys1.Count; i++)
			{
				except.Where
					.Expr(keys1[i].GetExpressions(this)[0])
					.Equal
					.Expr(keys2[i].GetExpressions(this)[0]);
			}

			return select;
		}

		#endregion

		#endregion

		#region SetQuery

		void SetQuery(ParseInfo info, QuerySource query, IndexConverter converter)
		{
			var table = query as QuerySource.Table;

			if (table != null)
				CurrentSql.Select.Columns.Clear();

			var idx = query.Select(this);

			if (table == null || table.InheritanceMapping.Count == 0)
			{
				foreach (var i in idx)
					converter(i);

				_info.SetQuery(null);
			}
			else
			{
				SetQuery(BuildTable(info, table, converter));
			}
		}

		void SetQuery(ParseInfo info)
		{
			SetQuery(info.Expr);
		}

		void SetQuery(Expression expr)
		{
			var mapper = Expression.Lambda<ExpressionInfo<T>.Mapper<T>>(
				expr, new[] { _infoParam, _contextParam, _dataReaderParam, _mapSchemaParam, _expressionParam, ParametersParam });

			_info.SetQuery(mapper.Compile());
		}

		#endregion

		#region Build Select

		#region BuildSelect

		void BuildSelect(QuerySource query, Action<QuerySource,IndexConverter> queryAction, Action<ParseInfo> newAction, IndexConverter converter)
		{
			ParsingTracer.WriteLine(query);
			ParsingTracer.IncIndentLevel();

			query.Match
			(
				table  => queryAction  (table, converter),                                // QueryInfo.Table
				expr   => BuildNew     (query, expr.Lambda.Body,   newAction),            // QueryInfo.Expr
				sub    => BuildSubQuery(sub,   queryAction,        newAction, converter), // QueryInfo.SubQuery
				scalar => BuildNew     (query, scalar.Lambda.Body, newAction),            // QueryInfo.Scalar
				group  => BuildGroupBy (group, group.Lambda.Body,  newAction)             // QueryInfo.GroupBy
			);

			ParsingTracer.DecIndentLevel();
		}

		void BuildNew(QuerySource query, ParseInfo expr, Action<ParseInfo> newAction)
		{
			ParsingTracer.WriteLine(expr);
			ParsingTracer.WriteLine(query);
			ParsingTracer.IncIndentLevel();

			var info = BuildNewExpression(query, expr, i => i);
			newAction(info);

			ParsingTracer.DecIndentLevel();
		}

		#endregion

		#region BuildSubQuerySource

		void BuildSubQuery(
			QuerySource                        subQuery,
			Action<QuerySource,IndexConverter> queryAction,
			Action<ParseInfo>                  newAction,
			IndexConverter                     converter)
		{
			ParsingTracer.WriteLine();
			ParsingTracer.IncIndentLevel();

			subQuery.BaseQuery.Match
			(
				table  => queryAction(table, i => converter(subQuery.EnsureField(i.Field).Select(this)[0])), // QueryInfo.Table
				expr   =>                                           // QueryInfo.Expr
				{
					if (expr.Lambda.Body.NodeType == ExpressionType.New)
						newAction(BuildQuerySourceExpr(subQuery, expr.Lambda.Body, converter));
					else
						throw new NotImplementedException();
				}, 
				_      => { throw new NotImplementedException(); }, // QueryInfo.SubQuery
				scalar =>                                           // QueryInfo.Scalar
				{
					var idx  = subQuery.Fields[0].Select(this);
					var info = BuildField(scalar.Lambda.Body, idx.Select(i => converter(i).Index).ToArray());
					newAction(info);
				},
				_      => { throw new NotImplementedException(); }  // QueryInfo.GroupBy
			);

			ParsingTracer.DecIndentLevel();
		}

		ParseInfo BuildQuerySourceExpr(QuerySource query, ParseInfo parseInfo, IndexConverter converter)
		{
			ParsingTracer.WriteLine(parseInfo);
			ParsingTracer.WriteLine(query);
			ParsingTracer.IncIndentLevel();

			NewExpression newExpr = null;
			var           member  = 0;

			var result = parseInfo.Walk(pi =>
			{
				if (newExpr == null && pi.NodeType == ExpressionType.New)
				{
					newExpr = (NewExpression)pi.Expr;
				}
				else if (newExpr != null)
				{
					var mi = newExpr.Members[member++];

					if (mi is MethodInfo)
						mi = TypeHelper.GetPropertyByMethod((MethodInfo)mi);

					var field = query.GetField(mi);
					var idx   = field.Select(this);

					return BuildField(pi, idx.Select(i => converter(i).Index).ToArray());
				}

				return pi;
			});

			ParsingTracer.DecIndentLevel();
			return result;
		}

		#endregion

		#region BuildGroupBy

		interface IGroupByHelper
		{
			ParseInfo GetParseInfo(ExpressionParser<T> parser, QuerySource.GroupBy query, ParseInfo expr, Expression info);
		}

		class GroupByHelper<TKey,TElement,TSource> : IGroupByHelper
		{
			public ParseInfo GetParseInfo(ExpressionParser<T> parser, QuerySource.GroupBy query, ParseInfo expr, Expression info)
			{
				var valueParser = new ExpressionParser<TElement>();
				var keyParam    = Expression.Convert(Expression.ArrayIndex(ParametersParam, Expression.Constant(0)), typeof(TKey));

				Expression valueExpr = null;

				if (expr.NodeType == ExpressionType.New)
				{
					var ne = (NewExpression)expr.Expr;

					for (var i = 0; i < ne.Arguments.Count; i++)
					{
						var member = TypeHelper.GetPropertyByMethod((MethodInfo)ne.Members[i]);
						var equal  = Expression.Equal(ne.Arguments[i], Expression.MakeMemberAccess(keyParam, member));

						valueExpr = valueExpr == null ? equal : Expression.AndAlso(valueExpr, equal);
					}
				}
				else if (query.BaseQuery is QuerySource.Table)
				{
					var table  = (QuerySource.Table)query.BaseQuery;
					var parent = table.ParentAssociation;
					var pexpr  = ((MemberExpression)expr.Expr).Expression;
					var conds = table.ParentAssociationJoin.Condition.Conditions;

					foreach (var cond in conds)
					{
						var ee = (SqlQuery.Predicate.ExprExpr)cond.Predicate;

						var equal  = Expression.Equal(
							Expression.MakeMemberAccess(pexpr,    parent.Columns[((SqlField)ee.Expr1).Name].Mapper.MemberAccessor.MemberInfo),
							Expression.MakeMemberAccess(keyParam, table. Columns[((SqlField)ee.Expr2).Name].Mapper.MemberAccessor.MemberInfo));

						valueExpr = valueExpr == null ? equal : Expression.AndAlso(valueExpr, equal);
					}
				}
				else
				{
					valueExpr = Expression.Equal(query.Lambda.Body, keyParam);
				}

// ReSharper disable AssignNullToNotNullAttribute
				valueExpr = Expression.Call(
					null,
					Expressor<object>.MethodExpressor(_ => Queryable.Where(null, (Expression<Func<TSource,bool>>)null)),
					query.OriginalQuery.Lambda.Body.Expr,
					Expression.Lambda<Func<TSource,bool>>(valueExpr, new[] { query.Lambda.Parameters[0].Expr }));

				if (query.ElementSource != null)
				{
					valueExpr = Expression.Call(
						null,
						Expressor<object>.MethodExpressor(_ => Queryable.Select(null, (Expression<Func<TSource,TElement>>)null)),
						valueExpr,
						Expression.Lambda<Func<TSource,TElement>>(query.ElementSource.Lambda.Body, new[] { query.ElementSource.Lambda.Parameters[0].Expr }));
				}
// ReSharper restore AssignNullToNotNullAttribute

				var keyReader = Expression.Lambda<ExpressionInfo<T>.Mapper<TKey>>(
					info, new[]
					{
						parser._infoParam,
						parser._contextParam,
						parser._dataReaderParam,
						parser._mapSchemaParam,
						parser._expressionParam,
						ParametersParam
					});

				return expr.Parent.Replace(
					Expression.Call(parser._infoParam, parser._info.GetGroupingMethodInfo<TKey,TElement>(),
						parser._contextParam,
						parser._dataReaderParam,
						parser._expressionParam,
						ParametersParam,
						Expression.Constant(keyReader.Compile()),
						Expression.Constant(valueParser.Parse(parser._info.DataProvider, parser._info.MappingSchema, valueExpr, parser._info.Parameters))),
					expr.ParamAccessor);
			}
		}

		void BuildGroupBy(QuerySource.GroupBy query, ParseInfo expr, Action<ParseInfo> newAction)
		{
			ParsingTracer.WriteLine(expr);
			ParsingTracer.WriteLine(query);
			ParsingTracer.IncIndentLevel();

			ParseInfo info;

			if (query.BaseQuery is QuerySource.Table)
			{
				var table = (QuerySource.Table)query.BaseQuery;
				var conds = table.ParentAssociationJoin.Condition.Conditions;
				var index = new int[table.Fields.Count];

				for (var i = 0; i < index.Length; i++)
					index[i] = -1;

				foreach (var cond in conds)
				{
					var field = (SqlField)((SqlQuery.Predicate.ExprExpr)cond.Predicate).Expr2;

					index[table.Fields.IndexOf(table.Columns[field.Name])] = query.GetField(field).Select(this)[0].Index;
				}

				info = ParseInfo.CreateRoot(
					Expression.Convert(
						Expression.Call(_infoParam, _info.GetMapperMethodInfo(),
							Expression.Constant(expr.Expr.Type),
							_dataReaderParam,
							Expression.Constant(_info.GetMapperSlot(index))),
						expr.Expr.Type),
					expr);
			}
			else if (query.IsWrapped && expr.NodeType != ExpressionType.New)
			{
				var idx = query.Fields[0].Select(this);

				if (idx.Length != 1)
					throw new InvalidOperationException();

				info = BuildField(expr, new[] { idx[0].Index });
			}
			else
				info = BuildNewExpression(query, expr, i => i);

			var args   = query.GroupingType.GetGenericArguments();
			var helper = (IGroupByHelper)Activator.CreateInstance(typeof(GroupByHelper<,,>).
				MakeGenericType(typeof(T), args[0], args[1], query.Lambda.Parameters[0].Expr.Type));

			info = helper.GetParseInfo(this, query, expr, info);
			newAction(info);

			ParsingTracer.DecIndentLevel();
		}

		#endregion

		#region BuildNewExpression

		ParseInfo BuildNewExpression(QuerySource query, ParseInfo expr, IndexConverter converter)
		{
			ParsingTracer.WriteLine(expr);
			ParsingTracer.WriteLine(query);
			ParsingTracer.IncIndentLevel();

			var newExpr = expr.Walk(pi =>
			{
				switch (pi.NodeType)
				{
					case ExpressionType.MemberAccess:
						{
							if (IsServerSideOnly(pi))        return BuildField   (query, pi);
							if (IsSubQuery      (pi, query)) return BuildSubQuery(pi, query, converter);

							var ma = (ParseInfo<MemberExpression>)pi;
							var ex = pi.Create(ma.Expr.Expression, pi.Property(Member.Expression));

							if (query.Sources.Length > 0)
							{
								var field = query.GetBaseField(ma);

								if (field != null)
								{
									if (field is QueryField.Column)
										return BuildField(ma, field, converter);

									if (field is QuerySource.SubQuery)
										return BuildSubQuerySource(ma, (QuerySource.SubQuery)field, converter);

									if (field is QueryField.ExprColumn)
									{
										var col = (QueryField.ExprColumn)field;

										pi = BuildNewExpression(col.QuerySource, col.Expr, converter);
										pi.IsReplaced = pi.StopWalking = true;

										return pi;
									}

									if (field is QuerySource.Table)
										return BuildTable(ma, (QuerySource.Table)field, converter);

									if (field is QueryField.SubQueryColumn)
										return BuildSubQuerySource(ma, (QueryField.SubQueryColumn)field, converter);

									if (field is QueryField.GroupByColumn)
									{
										var ret = BuildGroupBy(ma, (QueryField.GroupByColumn)field, converter);
										ret.StopWalking = true;
										return ret;
									}

									throw new InvalidOperationException();
								}

								if (ex.Expr != null && query is QuerySource.Scalar && ex.NodeType == ExpressionType.Constant)
									return BuildField(query, ma);
							}
							else
							{
								var field = GetField(ma, query);

								if (field != null)
									return BuildField(ma, field, converter/*i => i*/);
							}

							if (ex.Expr != null && ex.NodeType == ExpressionType.Constant)
							{
								// field = localVariable
								//
								var c = ex.Parent.Create((ConstantExpression)ex.Expr, ex.Property<ConstantExpression>(Constant.Value));

								return pi.Parent.Replace(
									Expression.MakeMemberAccess(
										Expression.Convert(c.ParamAccessor, ex.Expr.Type),
										ma.Expr.Member),
									c.ParamAccessor);
							}

							break;
						}

					case ExpressionType.Parameter:
						{
							if (pi.Expr == ParametersParam)
							{
								pi.StopWalking = true;
								return pi;
							}

							var field = query.GetBaseField(pi.Expr);

							if (field != null)
							{
								if (field is QuerySource.Table)
									return BuildTable(pi, (QuerySource.Table)field, converter);

								if (field is QuerySource.Scalar)
								{
									var source = (QuerySource)field;
									return BuildNewExpression(source, source.Lambda.Body, converter);
								}

								if (field is QuerySource.Expr)
								{
									var source = (QuerySource)field;
									return BuildQuerySourceExpr(query, source.Lambda.Body, converter);
								}

								if (field is QuerySource.GroupJoin)
									return BuildGroupJoin(pi, (QuerySource.GroupJoin)field, converter);

								if (field is QuerySource.SubQuery)
									return BuildSubQuerySource(pi, (QuerySource.SubQuery)field, converter);

								throw new InvalidOperationException();
							}

							break;
						}

					case ExpressionType.Constant:
						{
							if (IsConstant(pi.Expr.Type))
								break;

							if (query.Sources.Length == 0)
							{
								var field = GetField(pi, query);

								if (field != null)
								{
									var idx = field.Select(this);
									return BuildField(pi, idx.Select(i => converter(i).Index).ToArray());
								}
							}

							if (query is QuerySource.Scalar && CurrentSql.Select.Columns.Count == 0)
								return BuildField(query, pi);

							break;
						}

					case ExpressionType.Coalesce:
					//case ExpressionType.Conditional:
						if (pi.Expr.Type == typeof(string) && _info.MappingSchema.GetDefaultNullValue<string>() != null)
							return BuildField(query.BaseQuery, pi);
						break;

					case ExpressionType.Call:
						{
							if (IsServerSideOnly(pi))        return BuildField   (query, pi);
							if (IsSubQuery      (pi, query)) return BuildSubQuery(pi, query, converter);
						}

						break;

				}

				return pi;
			});

			ParsingTracer.DecIndentLevel();
			return newExpr;
		}

		#endregion

		#region BuildSubQuery

		ParseInfo BuildSubQuery(ParseInfo expr, QuerySource query, IndexConverter converter)
		{
			ParsingTracer.WriteLine(expr);
			ParsingTracer.WriteLine(query);
			ParsingTracer.IncIndentLevel();

			_parentQueries.Insert(0, new ParentQuery { Parent = query, Parameter = query.Lambda.Parameters[0]});
			var sql = CurrentSql;

			CurrentSql = new SqlQuery { ParentSql = sql };

			var seq = ParseSequence(expr)[0];

			if (seq.Fields.Count == 1 && CurrentSql.Select.Columns.Count == 0)
				seq.Fields[0].Select(this);

			var column = new QueryField.ExprColumn(query, CurrentSql, null);

			query.Fields.Add(column);

			var idx    = column.Select(this);
			var result = BuildField(expr, idx.Select(i => converter(i).Index).ToArray());

			CurrentSql = sql;
			_parentQueries.RemoveAt(0);

			ParsingTracer.DecIndentLevel();

			return result;
		}

		#endregion

		#region BuildTable

		static object DefaultInheritanceMappingException(object value, Type type)
		{
			throw new LinqException("Inheritance mapping is not defined for discriminator value '{0}' in the '{1}' hierarchy.", value, type);
		}

		ParseInfo BuildTable(ParseInfo pi, QuerySource.Table table, IndexConverter converter)
		{
			ParsingTracer.WriteLine(pi);
			ParsingTracer.WriteLine(table);
			ParsingTracer.IncIndentLevel();

			var objectType = table.ObjectType;

			if (table.InheritanceMapping.Count > 0 && pi.Expr.Type.IsGenericType)
			{
				var types = pi.Expr.Type.GetGenericArguments();

				if (types.Length == 1 && TypeHelper.IsSameOrParent(objectType, types[0]))
					objectType = types[0];
			}

			var mapperMethod = _info.GetMapperMethodInfo();

			Func<Type,int[],UnaryExpression> makeExpr = (ty,idx) =>
				Expression.Convert(
					Expression.Call(_infoParam, mapperMethod,
						Expression.Constant(ty),
						_dataReaderParam,
						Expression.Constant(_info.GetMapperSlot(idx))),
					objectType);

			Func<Type,int[]> makeIndex = ty =>
			{
				var q =
					from mm in _info.MappingSchema.GetObjectMapper(ty)
					where !mm.MapMemberInfo.SqlIgnore
					select converter(table.Columns[mm.MemberName].Select(this)[0]).Index;

				return q.ToArray();
			};

			Expression expr;

			if (objectType != table.ObjectType)
			{
				expr = makeExpr(objectType, makeIndex(objectType));
			}
			else if (table.InheritanceMapping.Count == 0)
			{
				expr = makeExpr(objectType, table.Select(this).Select(i => converter(i).Index).ToArray());

				if (table.ParentAssociation != null && table.ParentAssociationJoin.JoinType == SqlQuery.JoinType.Left)
				{
					Expression cond = null;

					foreach (var c in table.ParentAssociationJoin.Condition.Conditions)
					{
						var ee = (SqlQuery.Predicate.ExprExpr)c.Predicate;

						var field1  = (SqlField)ee.Expr1;
						var column1 = (QueryField.Column)table.ParentAssociation.GetField(field1);
						var index1  = column1.Select(this)[0].Index;

						var field2  = (SqlField)ee.Expr2;
						var column2 = (QueryField.Column)table.ParentAssociation.GetField(field2);
						var index2  = column2.Select(this)[0].Index;

						var e =
							Expression.AndAlso(
								Expression.Call(_dataReaderParam, DataReader.IsDBNull, Expression.Constant(index2)),
								Expression.Not(
									Expression.Call(_dataReaderParam, DataReader.IsDBNull, Expression.Constant(index1))));

						cond = cond == null ? e : Expression.AndAlso(cond, e);
					}

					expr = Expression.Condition(cond, Expression.Convert(Expression.Constant(null), objectType), expr);
				}
			}
			else
			{
				var defaultMapping = table.InheritanceMapping.SingleOrDefault(m => m.IsDefault);

				if (defaultMapping != null)
				{
					expr = makeExpr(defaultMapping.Type, makeIndex(defaultMapping.Type));
				}
				else
				{
					var exceptionMethod = Expressor<ExpressionParser<T>>.MethodExpressor(_ => DefaultInheritanceMappingException(null, null));
					var dindex         = table.Columns[table.InheritanceDiscriminators[0]].Select(this)[0].Index;

					expr = Expression.Convert(
						Expression.Call(_infoParam, exceptionMethod,
							Expression.Call(_dataReaderParam, DataReader.GetValue, Expression.Constant(dindex)),
							Expression.Constant(table.ObjectType)),
						table.ObjectType);
				}

				foreach (var mapping in table.InheritanceMapping.Select((m,i) => new { m, i }).Where(m => m.m != defaultMapping))
				{
					var dindex = table.Columns[table.InheritanceDiscriminators[mapping.i]].Select(this)[0].Index;
					Expression testExpr;

					if (mapping.m.Code == null)
					{
						testExpr = Expression.Call(_dataReaderParam, DataReader.IsDBNull, Expression.Constant(dindex));
					}
					else
					{
						MethodInfo mi;
						var codeType = mapping.m.Code.GetType();

						if (!MapSchema.Converters.TryGetValue(codeType, out mi))
							throw new LinqException("Cannot find converter for the '{0}' type.", codeType.FullName);

						testExpr =
							Expression.Equal(
								Expression.Constant(mapping.m.Code),
								Expression.Call(_mapSchemaParam, mi, Expression.Call(_dataReaderParam, DataReader.GetValue, Expression.Constant(dindex))));
					}

					expr = Expression.Condition(testExpr, makeExpr(mapping.m.Type, makeIndex(mapping.m.Type)), expr);
				}
			}

			var field = pi.Parent == null ?
				pi.       Replace(expr, pi.ParamAccessor) :
				pi.Parent.Replace(expr, pi.ParamAccessor);

			ParsingTracer.DecIndentLevel();
			return field;
		}

		#endregion

		#region BuildSubQuerySource

		ParseInfo BuildSubQuerySource(ParseInfo ma, QuerySource.SubQuery query, IndexConverter converter)
		{
			ParsingTracer.WriteLine(ma);
			ParsingTracer.WriteLine(query);
			ParsingTracer.IncIndentLevel();

			ParseInfo result = null;

			if (query is QuerySource.GroupJoin && TypeHelper.IsSameOrParent(typeof(IEnumerable), ma.Expr.Type))
			{
				result = BuildGroupJoin(ma, (QuerySource.GroupJoin)query, converter);
			}
			else if (query.Sources.Length == 1)
			{
				var baseQuery = query.BaseQuery;

				Func<FieldIndex,FieldIndex> conv = i => converter(query.EnsureField(i.Field).Select(this)[0]);

				if (baseQuery is QuerySource.Table)
				{
					result = BuildTable(ma, (QuerySource.Table)baseQuery, conv);
				}
				else if (baseQuery is QuerySource.SubQuery)
				{
					result = BuildSubQuerySource(ma, (QuerySource.SubQuery)baseQuery, conv);
				}
				else if (baseQuery is QuerySource.Scalar)
				{
					var idx = query.Select(this);
					result = BuildField(ma, idx.Select(i => converter(i).Index).ToArray());
				}
				else
					result = BuildNewExpression(baseQuery, baseQuery.Lambda.Body, conv);

				if (query is QuerySource.GroupJoin)
				{
					var join  = (QuerySource.GroupJoin)query;
					var check = join.CheckNullField;
					var idx   = converter(check.Select(this)[0]);

					result = result.Replace(
						Expression.Condition(
							Expression.Call(_dataReaderParam, DataReader.IsDBNull, Expression.Constant(idx.Index)),
							Expression.Convert(
								Expression.Constant(_info.MappingSchema.GetNullValue(result.Expr.Type)),
								result.Expr.Type),
							result.Expr),
						result.ParamAccessor);
				}
			}

			if (result == null)
				throw new InvalidOperationException();

			ParsingTracer.DecIndentLevel();

			return result;
		}

		ParseInfo BuildSubQuerySource(ParseInfo ma, QueryField.SubQueryColumn query, IndexConverter converter)
		{
#if DEBUG
			ParsingTracer.WriteLine(ma);
			ParsingTracer.WriteLine(query);
			ParsingTracer.IncIndentLevel();

			try
			{
#endif
				IndexConverter conv = i => converter(query.QuerySource.EnsureField(i.Field).Select(this)[0]);

				if (query.Field is QuerySource.Table)
					return BuildTable(ma, (QuerySource.Table)query.Field, conv);

				if (query.Field is QuerySource.SubQuery)
					return BuildSubQuerySource(ma, (QuerySource.SubQuery)query.Field, conv);

				if (query.Field is QuerySource)
					throw new InvalidOperationException();

				if (query.Field is QueryField.SubQueryColumn)
					return BuildSubQuerySource(ma, (QueryField.SubQueryColumn)query.Field, conv);

				return BuildField(ma, query, converter);
#if DEBUG
			}
			finally
			{
				ParsingTracer.DecIndentLevel();
			}
#endif
		}

		#endregion

		#region BuildGroupJoin

		interface IGroupJoinHelper
		{
			ParseInfo GetParseInfo(ExpressionParser<T> parser, ParseInfo ma, FieldIndex counterIndex, Expression info);
		}

		class GroupJoinHelper<TE> : IGroupJoinHelper
		{
			public ParseInfo GetParseInfo(ExpressionParser<T> parser, ParseInfo ma, FieldIndex counterIndex, Expression info)
			{
				var itemReader = Expression.Lambda<ExpressionInfo<T>.Mapper<TE>>(
					info, new[]
					{
						parser._infoParam,
						parser._contextParam,
						parser._dataReaderParam,
						parser._mapSchemaParam,
						parser._expressionParam,
						ParametersParam
					});

				return ma.Parent.Replace(
					Expression.Call(parser._infoParam, parser._info.GetGroupJoinEnumeratorMethodInfo<TE>(),
						parser._contextParam,
						parser._dataReaderParam,
						parser._expressionParam,
						ParametersParam,
						Expression.Constant(counterIndex.Index),
						Expression.Constant(itemReader.Compile())),
					ma.ParamAccessor);
			}
		}

		ParseInfo BuildGroupJoin(ParseInfo ma, QuerySource.GroupJoin query, IndexConverter converter)
		{
			ParsingTracer.WriteLine(ma);
			ParsingTracer.WriteLine(query);
			ParsingTracer.IncIndentLevel();

			var args = ma.Expr.Type.GetGenericArguments();

			if (args.Length == 0)
				return BuildSubQuerySource(ma, query, converter);

			Expression expr = null;

			BuildSelect(
				query.BaseQuery,
				(q, c) =>
				{
					var index = q.Select(this).Select(i => c(i).Index).ToArray();

					expr = Expression.Convert(
						Expression.Call(_infoParam, _info.GetMapperMethodInfo(),
							Expression.Constant(args[0]),
							_dataReaderParam,
							Expression.Constant(_info.GetMapperSlot(index))),
						args[0]);
				},
				info  => expr = info,
				i     => converter(query.EnsureField(i.Field).Select(this)[0]));

			var helper       = (IGroupJoinHelper)Activator.CreateInstance(typeof(GroupJoinHelper<>).MakeGenericType(typeof(T), args[0]));
			var counterIndex = converter(query.Counter.Select(this)[0]);

			var result       = helper.GetParseInfo(this, ma, counterIndex, expr);
			ParsingTracer.DecIndentLevel();
			return result;
		}

		#endregion

		#region BuildGroupBy

		ParseInfo BuildGroupBy(ParseInfo<MemberExpression> ma, QueryField.GroupByColumn field, IndexConverter converter)
		{
			ParsingTracer.WriteLine(ma);
			ParsingTracer.WriteLine(field);
			ParsingTracer.IncIndentLevel();

			var source = field.GroupBySource.BaseQuery;

			if (source is QuerySource.Scalar)
				return BuildField(ma, field.GroupBySource, converter);

			if (source is QuerySource.SubQuery)
			{
				if (source.BaseQuery is QuerySource.Scalar)
					return BuildField(ma, field.GroupBySource, converter);

				return BuildNewExpression(source, field.GroupBySource.Lambda.Body, converter /*i => converter(source.EnsureField(i.Field).Select(this)[0])*/);
			}

			var result = BuildNewExpression(source, field.GroupBySource.Lambda.Body, converter);
			ParsingTracer.DecIndentLevel();
			return result;
		}

		#endregion

		#region BuildField

		ParseInfo BuildField(QuerySource query, ParseInfo pi)
		{
			ParsingTracer.WriteLine(pi);
			ParsingTracer.WriteLine(query);
			ParsingTracer.IncIndentLevel();

			var sqlex = ParseExpression(pi, query);
			var idx   = CurrentSql.Select.Add(sqlex);
			var field = BuildField(pi, new[] { idx });

			ParsingTracer.IncIndentLevel();
			return field;
		}

		ParseInfo BuildField(ParseInfo ma, QueryField field, IndexConverter converter)
		{
			ParsingTracer.WriteLine(ma);
			ParsingTracer.WriteLine(field);
			ParsingTracer.IncIndentLevel();

			ParseInfo result = null;

			if (field is QuerySource.SubQuery)
			{
				var query = (QuerySource.SubQuery)field;

				if (query is QuerySource.GroupJoin && TypeHelper.IsSameOrParent(typeof(IEnumerable), ma.Expr.Type))
					result = BuildGroupJoin(ma, (QuerySource.GroupJoin)query, converter);
				else if (query.BaseQuery is QuerySource.Table)
				{
					var table = (QuerySource.Table)query.BaseQuery;

					if (ma.Expr.Type == table.ObjectType)
						result = BuildTable(ma, table, i => converter(query.EnsureField(i.Field).Select(this)[0]));
				}
			}

			if (result == null)
			{
				var idx = field.Select(this);
				result = BuildField(ma, idx.Select(i => converter(i).Index).ToArray());
			}

			ParsingTracer.DecIndentLevel();
			return result;
		}

		ParseInfo BuildField(ParseInfo ma, int[] idx)
		{
			ParsingTracer.WriteLine(ma);
			ParsingTracer.IncIndentLevel();

			if (idx.Length != 1)
				throw new InvalidOperationException();

			MethodInfo mi;

			var type = ma.Expr.Type;

			//if (type.IsEnum)
			//	type = Enum.GetUnderlyingType(type);

			if (!MapSchema.Converters.TryGetValue(type, out mi))
				throw new LinqException("Cannot find converter for the '{0}' type.", type.FullName);

			var mapper = Expression.Call(_mapSchemaParam, mi, Expression.Call(_dataReaderParam, DataReader.GetValue, Expression.Constant(idx[0])));
			var result = ma.Parent == null ? ma.Create(mapper, ma.ParamAccessor) : ma.Parent.Replace(mapper, ma.ParamAccessor);

			ParsingTracer.DecIndentLevel();
			return result;
		}

		#endregion

		#endregion

		#region BuildSimpleQuery

		bool BuildSimpleQuery(ParseInfo info, Type type, string alias)
		{
			_isParsingPhase = false;

			var table = CreateTable(CurrentSql, type);

			table.SqlTable.Alias = alias;

			SetQuery(info, table, i => i);

			return true;
		}

		#endregion

		#region Build Scalar Select

		void BuildScalarSelect(ParseInfo parseInfo)
		{
			_isParsingPhase = false;

			switch (parseInfo.NodeType)
			{
				case ExpressionType.New:
				case ExpressionType.MemberInit:
					var query = ParseSelect(new LambdaInfo(parseInfo));

					query.Select(this);
					BuildNew(query, parseInfo, SetQuery);
					return;
			}

			var expr = ParseExpression(parseInfo);

			CurrentSql.Select.Expr(expr);

			var pi = BuildField(parseInfo, new[] { 0 });

			var mapper = Expression.Lambda<ExpressionInfo<T>.Mapper<T>>(
				pi, new [] { _infoParam, _contextParam, _dataReaderParam, _mapSchemaParam, _expressionParam, ParametersParam });

			_info.SetQuery(mapper.Compile());
		}

		#endregion

		#region Build Constant

		readonly Dictionary<Expression,SqlValue> _constants = new Dictionary<Expression,SqlValue>();

		SqlValue BuildConstant(ParseInfo expr)
		{
			SqlValue value;

			if (_constants.TryGetValue(expr.Expr, out value))
				return value;

			var lambda = Expression.Lambda<Func<object>>(Expression.Convert(expr,typeof(object)));

			value = new SqlValue(lambda.Compile()());

			_constants.Add(expr.Expr, value);

			return value;
		}

		#endregion

		#region Build Parameter

		readonly Dictionary<Expression,ExpressionInfo<T>.Parameter> _parameters = new Dictionary<Expression, ExpressionInfo<T>.Parameter>();
		readonly Dictionary<Expression,Expression>                  _accessors  = new Dictionary<Expression, Expression>();

		ExpressionInfo<T>.Parameter BuildParameter(ParseInfo expr)
		{
			ExpressionInfo<T>.Parameter p;

			if (_parameters.TryGetValue(expr.Expr, out p))
				return p;

			string name = null;

			var newExpr = ReplaceParameter(expr, nm => name = nm);
			var mapper  = Expression.Lambda<Func<ExpressionInfo<T>,Expression,object[],object>>(
				Expression.Convert(newExpr, typeof(object)),
				new [] { _infoParam, _expressionParam, ParametersParam });

			p = new ExpressionInfo<T>.Parameter
			{
				Expression   = expr.Expr,
				Accessor     = mapper.Compile(),
				SqlParameter = new SqlParameter(name, expr.Expr.Type, (object)null)
			};

			_parameters.Add(expr.Expr, p);
			CurrentSqlParameters.Add(p);

			return p;
		}

		ParseInfo ReplaceParameter(ParseInfo expr, Action<string> setName)
		{
			return expr.Walk(pi =>
			{
				if (pi.NodeType == ExpressionType.MemberAccess)
				{
					Expression accessor;

					if (_accessors.TryGetValue(pi.Expr, out accessor))
					{
						var ma = (MemberExpression)pi.Expr;
						setName(ma.Member.Name);

						return pi.Parent.Replace(pi.Expr, accessor);
					}
				}

				pi.IsConstant(c =>
				{
					if (!TypeHelper.IsScalar(pi.Expr.Type))
					{
						var e = Expression.Convert(c.ParamAccessor, pi.Expr.Type);
						pi = pi.Parent.Replace(e, c.ParamAccessor);

						if (pi.Parent.NodeType == ExpressionType.MemberAccess)
						{
							var ma = (MemberExpression)pi.Parent.Expr;
							setName(ma.Member.Name);
						}
					}

					return true;
				});

				return pi;
			});
		}

		#endregion

		#region Expression Parser

		#region ParseExpression

		public ISqlExpression ParseExpression(ParseInfo parseInfo, params QuerySource[] queries)
		{
			ParsingTracer.WriteLine(parseInfo);
			ParsingTracer.WriteLine(queries);
			ParsingTracer.IncIndentLevel();

			try
			{
				var qlen = queries.Length;

				if (parseInfo.NodeType == ExpressionType.Parameter && qlen == 1 && queries[0] is QuerySource.Scalar)
				{
					var ma = (QuerySource.Scalar)queries[0];
					return ParseExpression(ma.Lambda.Body, ma.Sources);
				}

				if (CanBeConstant(parseInfo))
					return BuildConstant(parseInfo);

				if (CanBeCompiled(parseInfo))
					return BuildParameter(parseInfo).SqlParameter;

				if (IsSubQuery(parseInfo, queries))
					return ParseSubQuery(parseInfo, queries);

				switch (parseInfo.NodeType)
				{
					case ExpressionType.AndAlso:
					case ExpressionType.OrElse:
					case ExpressionType.Not:
					case ExpressionType.Equal:
					case ExpressionType.NotEqual:
					case ExpressionType.GreaterThan:
					case ExpressionType.GreaterThanOrEqual:
					case ExpressionType.LessThan:
					case ExpressionType.LessThanOrEqual:
						{
							var condition = new SqlQuery.SearchCondition();
							ParseSearchCondition(condition.Conditions, null, parseInfo, queries);
							return condition;
						}

					case ExpressionType.Add:
					case ExpressionType.AddChecked:
					case ExpressionType.And:
					case ExpressionType.Divide:
					case ExpressionType.ExclusiveOr:
					case ExpressionType.Modulo:
					case ExpressionType.Multiply:
					case ExpressionType.Or:
					case ExpressionType.Power:
					case ExpressionType.Subtract:
					case ExpressionType.SubtractChecked:
					case ExpressionType.Coalesce:
						{
							var pi = parseInfo.Convert<BinaryExpression>();
							var e  = parseInfo.Expr as BinaryExpression;
							var l  = ParseExpression(pi.Create(e.Left,  pi.Property(Binary.Left)),  queries);
							var r  = ParseExpression(pi.Create(e.Right, pi.Property(Binary.Right)), queries);
							var t  = e.Left.Type ?? e.Right.Type;

							switch (parseInfo.NodeType)
							{
								case ExpressionType.Add            :
								case ExpressionType.AddChecked     : return Convert(new SqlBinaryExpression(l, "+", r, t, Precedence.Additive));
								case ExpressionType.And            : return Convert(new SqlBinaryExpression(l, "&", r, t, Precedence.Bitwise));
								case ExpressionType.Divide         : return Convert(new SqlBinaryExpression(l, "/", r, t, Precedence.Multiplicative));
								case ExpressionType.ExclusiveOr    : return Convert(new SqlBinaryExpression(l, "^", r, t, Precedence.Bitwise));
								case ExpressionType.Modulo         : return Convert(new SqlBinaryExpression(l, "%", r, t, Precedence.Multiplicative));
								case ExpressionType.Multiply       : return Convert(new SqlBinaryExpression(l, "*", r, t, Precedence.Multiplicative));
								case ExpressionType.Or             : return Convert(new SqlBinaryExpression(l, "|", r, t, Precedence.Bitwise));
								case ExpressionType.Power          : return Convert(new SqlFunction("Power", l, r));
								case ExpressionType.Subtract       :
								case ExpressionType.SubtractChecked: return Convert(new SqlBinaryExpression(l, "-", r, t, Precedence.Subtraction));
								case ExpressionType.Coalesce       :
									{
										if (r is SqlFunction)
										{
											var c = (SqlFunction)r;

											if (c.Name == "Coalesce")
											{
												var parms = new ISqlExpression[c.Parameters.Length + 1];

												parms[0] = l;
												c.Parameters.CopyTo(parms, 1);

												return Convert(new SqlFunction("Coalesce", parms));
											}
										}

										return Convert(new SqlFunction("Coalesce", l, r));
									}
							}

							break;
						}

					case ExpressionType.Convert:
						{
							var pi = parseInfo.Convert<UnaryExpression>();
							var e  = parseInfo.Expr as UnaryExpression;
							var o  = ParseExpression(pi.Create(e.Operand, pi.Property(Unary.Operand)), queries);

							if (e.Method == null && e.IsLifted)
								return o;

							return Convert(new SqlFunction("$Convert$", new SqlDataType(e.Type), new SqlDataType(e.Operand.Type), o));
						}

					case ExpressionType.Conditional:
						{
							var pi = parseInfo.Convert<ConditionalExpression>();
							var e  = parseInfo.Expr as ConditionalExpression;
							var s  = ParseExpression(pi.Create(e.Test,    pi.Property(Conditional.Test)),    queries);
							var t  = ParseExpression(pi.Create(e.IfTrue,  pi.Property(Conditional.IfTrue)),  queries);
							var f  = ParseExpression(pi.Create(e.IfFalse, pi.Property(Conditional.IfFalse)), queries);

							if (f is SqlFunction)
							{
								var c = (SqlFunction)f;

								if (c.Name == "CASE")
								{
									var parms = new ISqlExpression[c.Parameters.Length + 2];

									parms[0] = s;
									parms[1] = t;
									c.Parameters.CopyTo(parms, 2);

									return Convert(new SqlFunction("CASE", parms));
								}
							}

							return Convert(new SqlFunction("CASE", s, t, f));
						}

					case ExpressionType.MemberAccess:
						{
							var pi = parseInfo.ConvertTo<MemberExpression>();
							var ma = (MemberExpression)parseInfo.Expr;
							var ef = _info.SqlProvider.ConvertMember(ma.Member);

							if (ef != null)
							{
								var pie = parseInfo.Parent.Replace(ef, null).Walk(wpi =>
								{
									if (wpi.NodeType == ExpressionType.Parameter)
									{
										var expr = ma.Expression;

										if (expr.NodeType == ExpressionType.MemberAccess)
											if (!_accessors.ContainsKey(expr))
												_accessors.Add(expr, pi.Property(Member.Expression));

										return pi.Create(expr, null);
									}

									return wpi;
								});

								return ParseExpression(pie, queries);
							}

							var attr = GetFunctionAttribute(ma.Member);

							if (attr != null)
								return Convert(attr.GetExpression(ma.Member));

							if (IsNullableValueMember(ma.Member))
								return ParseExpression(pi.Create(ma.Expression, pi.Property(Member.Expression)), queries);

							if (IsListCountMember(ma.Member))
							{
								var src = GetSource(null, pi.Create(ma.Expression, pi.Property(Member.Expression)), queries);
								if (src != null)
									return SqlFunction.CreateCount(src.SqlQuery);
							}

							goto case ExpressionType.Parameter;
						}

					case ExpressionType.Parameter:
						{
							var field = GetField(parseInfo.Expr, queries);

							if (field != null)
							{
								var exprs = field.GetExpressions(this);

								if (exprs == null)
									break;

								if (exprs.Length == 1)
									return exprs[0];

								throw new InvalidOperationException();
							}

							break;
						}

					case ExpressionType.Call:
						{
							var pi = parseInfo.ConvertTo<MethodCallExpression>();
							var e  = parseInfo.Expr as MethodCallExpression;

							if (e.Method.DeclaringType == typeof(Enumerable))
								return ParseEnumerable(pi, queries);

							var ef = _info.SqlProvider.ConvertMember(e.Method);

							if (ef != null)
							{
								var pie = parseInfo.Parent.Replace(ef, null).Walk(wpi =>
								{
									if (wpi.NodeType == ExpressionType.Parameter)
									{
										Expression       expr;
										Func<Expression> fparam;

										var pe = (ParameterExpression)wpi.Expr;

										if (pe.Name == "obj")
										{
											expr   = e.Object;
											fparam = () => pi.Property(MethodCall.Object);
										}
										else
										{
											var i  = int.Parse(pe.Name.Substring(1));
											expr   = e.Arguments[i];
											fparam = () => pi.Index(e.Arguments, MethodCall.Arguments, i);
										}

										if (expr.NodeType == ExpressionType.MemberAccess)
											if (!_accessors.ContainsKey(expr))
												_accessors.Add(expr, fparam());

										return pi.Create(expr, null);
									}

									return wpi;
								});

								return ParseExpression(pie, queries);
							}

							var attr = GetFunctionAttribute(e.Method);

							if (attr != null)
							{
								var parms = new List<ISqlExpression>();

								if (e.Object != null)
									parms.Add(ParseExpression(pi.Create(e.Object, pi.Property(MethodCall.Object)), queries));

								for (var i = 0; i < e.Arguments.Count; i++)
									parms.Add(ParseExpression(pi.Create(e.Arguments[i], pi.Index(e.Arguments, MethodCall.Arguments, i)), queries));

								return Convert(attr.GetExpression(e.Method, parms.ToArray()));
							}

							break;
						}
				}

				throw new LinqException("'{0}' cannot be converted to SQL.", parseInfo.Expr);
			}
			finally
			{
				ParsingTracer.DecIndentLevel();
			}
		}

		#endregion

		#region ParseEnumerable

		ISqlExpression ParseEnumerable(ParseInfo<MethodCallExpression> pi, params QuerySource[] queries)
		{
			ParsingTracer.WriteLine(pi);
			ParsingTracer.WriteLine(queries);
			ParsingTracer.IncIndentLevel();

			QueryField field = queries.Length == 1 && queries[0] is QuerySource.GroupBy ? queries[0] : null;

			if (field == null)
				field = GetField(pi.Expr.Arguments[0], queries);

			if (!(field is QuerySource.GroupBy))
				throw new LinqException("'{0}' cannot be converted to SQL.", pi.Expr);

			var groupBy = (QuerySource.GroupBy)field;
			var expr    = ParseEnumerable(pi, groupBy);

			if (queries.Length == 1 && queries[0] is QuerySource.SubQuery)
			{
				var subQuery  = (QuerySource.SubQuery)queries[0];
				var column    = groupBy.FindField(new QueryField.ExprColumn(groupBy, expr, null));
				var subColumn = subQuery.EnsureField(column);

				expr = subColumn.GetExpressions(this)[0];
			}

			ParsingTracer.DecIndentLevel();
			return expr;
		}

		ISqlExpression ParseEnumerable(ParseInfo<MethodCallExpression> pi, QuerySource.GroupBy query)
		{
			var groupBy = query.OriginalQuery;
			var expr    = pi.Expr;
			var args    = new ISqlExpression[expr.Arguments.Count - 1];

			if (expr.Method.Name == "Count")
			{
				if (args.Length > 0)
				{
					var predicate = ParsePredicate(null, ParseLambdaArgument(pi, 1), groupBy);

					groupBy.SqlQuery.Where.SearchCondition.Conditions.Add(new SqlQuery.Condition(false, predicate));

					var sql = groupBy.SqlQuery.Clone(o => !(o is SqlParameter));

					groupBy.SqlQuery.Where.SearchCondition.Conditions.RemoveAt(groupBy.SqlQuery.Where.SearchCondition.Conditions.Count - 1);

					sql.Select.Columns.Clear();

					if (_info.SqlProvider.IsSubQueryColumnSupported && _info.SqlProvider.IsCountSubQuerySupported)
					{
						for (var i = 0; i < sql.GroupBy.Items.Count; i++)
						{
							var item1 = sql.GroupBy.Items[i];
							var item2 = groupBy.SqlQuery.GroupBy.Items[i];
							var pr    = Convert(new SqlQuery.Predicate.ExprExpr(item1, SqlQuery.Predicate.Operator.Equal, item2));

							sql.Where.SearchCondition.Conditions.Add(new SqlQuery.Condition(false, pr));
						}

						sql.GroupBy.Items.Clear();
						sql.Select.Expr(SqlFunction.CreateCount(sql));
						sql.ParentSql = groupBy.SqlQuery;

						return sql;
					}

					var join = sql.WeakLeftJoin();

					groupBy.SqlQuery.From.Tables[0].Joins.Add(join.JoinedTable);

					for (var i = 0; i < sql.GroupBy.Items.Count; i++)
					{
						var item1 = sql.GroupBy.Items[i];
						var item2 = groupBy.SqlQuery.GroupBy.Items[i];
						var col   = sql.Select.Columns[sql.Select.Add(item1)];
						var pr    = Convert(new SqlQuery.Predicate.ExprExpr(col, SqlQuery.Predicate.Operator.Equal, item2));

						join.JoinedTable.Condition.Conditions.Add(new SqlQuery.Condition(false, pr));
					}

					sql.ParentSql = groupBy.SqlQuery;

					return new SqlFunction("Count", sql.Select.Columns[0]);
				}

				return SqlFunction.CreateCount(groupBy.SqlQuery);
			}

			for (var i = 1; i < expr.Arguments.Count; i++)
				args[i - 1] = ParseExpression(ParseLambdaArgument(pi, i), groupBy);

			return new SqlFunction(expr.Method.Name, args);
		}

		static ParseInfo ParseLambdaArgument(ParseInfo pi, int idx)
		{
			var expr = (MethodCallExpression)pi.Expr;
			var arg  = pi.Create(expr.Arguments[idx], pi.Index(expr.Arguments, MethodCall.Arguments, idx));
			
			arg.IsLambda<Expression>(new Func<ParseInfo<ParameterExpression>,bool>[]
				{ _ => true },
				body => { arg = body; return true; },
				_ => true);

			return arg;
		}

		#endregion

		#region ParseSubQuery

		ISqlExpression ParseSubQuery(ParseInfo expr, params QuerySource[] queries)
		{
			ParsingTracer.WriteLine(expr);
			ParsingTracer.WriteLine(queries);
			ParsingTracer.IncIndentLevel();

			var parentQueries = queries.Select(q => new ParentQuery { Parent = q, Parameter = q.Lambda.Parameters.FirstOrDefault()}).ToList();

			_parentQueries.InsertRange(0, parentQueries);
			var sql = CurrentSql;

			CurrentSql = new SqlQuery { ParentSql = sql };

			var prev = _isSubQueryParsing;

			_isSubQueryParsing = true;

			var seq = ParseSequence(expr)[0];

			_isSubQueryParsing = prev;

			if (seq.Fields.Count == 1 && CurrentSql.Select.Columns.Count == 0)
			{
				//seq.Fields[0].Select(this);
			}

			var result = CurrentSql;

			CurrentSql = sql;
			_parentQueries.RemoveRange(0, parentQueries.Count);

			ParsingTracer.DecIndentLevel();

			return result;
		}

		#endregion

		#region IsSubQuery

		bool IsSubQuery(ParseInfo parseInfo, params QuerySource[] queries)
		{
			switch (parseInfo.NodeType)
			{
				case ExpressionType.Call:
					{
						var call = parseInfo.Expr as MethodCallExpression;

						if (call.Method.DeclaringType == typeof(Queryable) || call.Method.DeclaringType == typeof(Enumerable))
						{
							var pi  = parseInfo.ConvertTo<MethodCallExpression>();
							var arg = call.Arguments[0];

							if (arg.NodeType == ExpressionType.Call)
								return IsSubQuery(pi.Create(arg, pi.Index(call.Arguments, MethodCall.Arguments, 0)), queries);

							if (IsSubQuerySource(arg, queries))
								return true;
						}

						if (IsIEnumerableType(parseInfo.Expr))
							return !CanBeCompiled(parseInfo);
					}

					break;

				case ExpressionType.MemberAccess:
					{
						var ma  = (MemberExpression)parseInfo.Expr;

						if (IsSubQueryMember(parseInfo.Expr) && IsSubQuerySource(ma.Expression, queries))
							return !CanBeCompiled(parseInfo);

						if (IsIEnumerableType(parseInfo.Expr))
							return !CanBeCompiled(parseInfo);
					}

					break;
			}

			return false;
		}

		bool IsSubQuerySource(Expression expr, params QuerySource[] queries)
		{
			if (expr == null)
				return false;

			var tbl = GetSource(null, expr, queries) as QuerySource.Table;

			if (tbl != null)
				return true;

			while (expr != null && expr.NodeType == ExpressionType.MemberAccess)
				expr = ((MemberExpression)expr).Expression;

			return expr != null && expr.NodeType == ExpressionType.Constant;
		}

		static bool IsSubQueryMember(Expression expr)
		{
			switch (expr.NodeType)
			{
				case ExpressionType.Call:
					{
					}

					break;

				case ExpressionType.MemberAccess:
					{
						var ma = (MemberExpression)expr;

						if (IsListCountMember(ma.Member))
							return true;
					}

					break;
			}

			return false;
		}

		static bool IsIEnumerableType(Expression expr)
		{
			var type = expr.Type;
			return type.IsClass && type != typeof(string) && TypeHelper.IsSameOrParent(typeof(IEnumerable), type);
		}

		#endregion

		#region IsServerSideOnly

		bool IsServerSideOnly(ParseInfo parseInfo)
		{
			switch (parseInfo.NodeType)
			{
				case ExpressionType.MemberAccess:
					{
						var pi = parseInfo.ConvertTo<MemberExpression>();
						var ef = _info.SqlProvider.ConvertMember(pi.Expr.Member);

						if (ef != null)
							return IsServerSideOnly(pi.Parent.Replace(ef, null));

						var attr = GetFunctionAttribute(pi.Expr.Member);

						return attr != null && attr.ServerSideOnly;
					}

				case ExpressionType.Call:
					{
						var pi = parseInfo.ConvertTo<MethodCallExpression>();
						var e  = pi.Expr;

						if (e.Method.DeclaringType == typeof(Enumerable))
						{
							switch (e.Method.Name)
							{
								case "Count":
								case "Average":
								case "Min":
								case "Max":
								case "Sum":
									return IsQueryMember(e.Arguments[0]);
							}
						}
						else
						{
							var ef = _info.SqlProvider.ConvertMember(e.Method);

							if (ef != null)
								return IsServerSideOnly(pi.Parent.Replace(ef, null));

							var attr = GetFunctionAttribute(e.Method);

							return attr != null && attr.ServerSideOnly;
						}

						break;
					}
			}

			return false;
		}

		static bool IsQueryMember(Expression expr)
		{
			if (expr != null) switch (expr.NodeType)
			{
				case ExpressionType.Parameter    : return true;
				case ExpressionType.MemberAccess : return IsQueryMember(((MemberExpression)expr).Expression);
			}

			return false;
		}

		#endregion

		#region CanBeConstant

		bool CanBeConstant(ParseInfo expr)
		{
			var canbe = true;

			expr.Walk(pi =>
			{
				var ex = pi.Expr;

				if (ex is BinaryExpression || ex is UnaryExpression || ex.NodeType == ExpressionType.Convert)
					return pi;

				switch (ex.NodeType)
				{
					case ExpressionType.Constant:
						{
							var c = (ConstantExpression)ex;

							if (c.Value == null || IsConstant(ex.Type))
								return pi;

							break;
						}

					case ExpressionType.MemberAccess:
						{
							var ma = (MemberExpression)ex;

							if (IsConstant(ma.Member.DeclaringType))
								return pi;

							break;
						}

					case ExpressionType.Call:
						{
							var mc = (MethodCallExpression)ex;

							if (IsConstant(mc.Method.DeclaringType) || mc.Method.DeclaringType == typeof(object))
								return pi;

							var attr = GetFunctionAttribute(mc.Method);

							if (attr != null && !attr.ServerSideOnly)
								return pi;

							break;
						}
				}

				canbe = false;
				pi.StopWalking = true;

				return pi;
			});

			return canbe;
		}

		#endregion

		#region CanBeCompiled

		bool CanBeCompiled(ParseInfo expr)
		{
			var canbe = true;

			expr.Walk(pi =>
			{
				if (canbe)
				{
					canbe = !IsServerSideOnly(pi);

					if (canbe) switch (pi.NodeType)
					{
						case ExpressionType.Parameter:
							{
								var p = (ParameterExpression)pi.Expr;

								canbe = p == ParametersParam;
								break;
							}

						case ExpressionType.MemberAccess:
							{
								var ma   = (MemberExpression)pi.Expr;
								var attr = GetFunctionAttribute(ma.Member);

								canbe = attr == null  || !attr.ServerSideOnly;
								break;
							}

						case ExpressionType.Call:
							{
								var mc   = (MethodCallExpression)pi.Expr;
								var attr = GetFunctionAttribute(mc.Method);

								canbe = attr == null  || !attr.ServerSideOnly;
								break;
							}
					}
				}

				pi.StopWalking = !canbe;

				return pi;
			});

			return canbe;
		}

		#endregion

		#region IsConstant

		public static bool IsConstant(Type type)
		{
			if (type.IsEnum)
				return true;

			switch (Type.GetTypeCode(type))
			{
				case TypeCode.Int16   :
				case TypeCode.Int32   :
				case TypeCode.Int64   :
				case TypeCode.UInt16  :
				case TypeCode.UInt32  :
				case TypeCode.UInt64  :
				case TypeCode.SByte   :
				case TypeCode.Byte    :
				case TypeCode.Decimal :
				case TypeCode.Double  :
				case TypeCode.Single  :
				case TypeCode.Boolean :
				case TypeCode.String  :
				case TypeCode.Char    : return true;
				default               : return false;
			}
		}

		#endregion

		#endregion

		#region Predicate Parser

		ISqlPredicate ParsePredicate(LambdaInfo lambda, ParseInfo parseInfo, params QuerySource[] queries)
		{
			ParsingTracer.WriteLine(lambda);
			ParsingTracer.WriteLine(parseInfo);
			ParsingTracer.WriteLine(queries);
			ParsingTracer.IncIndentLevel();

			try
			{
				switch (parseInfo.NodeType)
				{
					case ExpressionType.Equal :
					case ExpressionType.NotEqual :
					case ExpressionType.GreaterThan :
					case ExpressionType.GreaterThanOrEqual :
					case ExpressionType.LessThan :
					case ExpressionType.LessThanOrEqual :
						{
							var pi = parseInfo.Convert<BinaryExpression>();
							var e  = parseInfo.Expr as BinaryExpression;
							var el = pi.Create(e.Left,  pi.Property(Binary.Left));
							var er = pi.Create(e.Right, pi.Property(Binary.Right));

							return ParseCompare(lambda, parseInfo.NodeType, el, er, queries);
						}

					case ExpressionType.Call:
						{
							var pi = parseInfo.Convert<MethodCallExpression>();
							var e  = pi.Expr as MethodCallExpression;

							ISqlPredicate predicate = null;

							if (e.Method.Name == "Equals" && e.Object != null && e.Arguments.Count == 1)
							{
								var el = pi.Create(e.Object,       pi.Property(MethodCall.Object));
								var er = pi.Create(e.Arguments[0], pi.Index(e.Arguments, MethodCall.Arguments, 0));

								return ParseCompare(lambda, ExpressionType.Equal, el, er, queries);
							}
							else if (e.Method.DeclaringType == typeof(string))
							{
								switch (e.Method.Name)
								{
									case "Contains"   : predicate = ParseLikePredicate(pi, "%", "%", queries); break;
									case "StartsWith" : predicate = ParseLikePredicate(pi, "",  "%", queries); break;
									case "EndsWith"   : predicate = ParseLikePredicate(pi, "%",  "", queries); break;
								}
							}
							else if (e.Method.DeclaringType == typeof(Enumerable))
							{
								switch (e.Method.Name)
								{
									case "Contains" : predicate = ParseInPredicate(pi, queries); break;
								}
							}
							else if (e.Method == Functions.String.Like11) predicate = ParseLikePredicate(pi, queries);
							else if (e.Method == Functions.String.Like12) predicate = ParseLikePredicate(pi, queries);
							else if (e.Method == Functions.String.Like21) predicate = ParseLikePredicate(pi, queries);
							else if (e.Method == Functions.String.Like22) predicate = ParseLikePredicate(pi, queries);

							if (predicate != null)
								return Convert(predicate);

							break;
						}

					case ExpressionType.Conditional:
						return Convert(new SqlQuery.Predicate.ExprExpr(
							ParseExpression(parseInfo, queries),
							SqlQuery.Predicate.Operator.Equal,
							new SqlValue(true)));

					case ExpressionType.MemberAccess:
						{
							var pi = parseInfo.Convert<MemberExpression>();
							var e  = pi.Expr as MemberExpression;

							if (e.Member.Name == "HasValue" && 
								e.Member.DeclaringType.IsGenericType && 
								e.Member.DeclaringType.GetGenericTypeDefinition() == typeof(Nullable<>))
							{
								var expr = ParseExpression(pi.Create(e.Expression, pi.Property(Member.Expression)), queries);
								return Convert(new SqlQuery.Predicate.IsNull(expr, true));
							}

							break;
						}

					case ExpressionType.TypeIs:
						{
							var pi = parseInfo.Convert<TypeBinaryExpression>();
							var e  = pi.Expr as TypeBinaryExpression;

							var table = GetSource(lambda, e.Expression, queries) as QuerySource.Table;

							if (table != null && table.InheritanceMapping.Count > 0)
								return MakeIsPredicate(table, e.TypeOperand);

							break;
						}
				}

				var ex = ParseExpression(parseInfo, queries);

				if (ex is SqlParameter)
					return Convert(new SqlQuery.Predicate.ExprExpr(ex, SqlQuery.Predicate.Operator.Equal, new SqlValue(true)));
				else
					return Convert(new SqlQuery.Predicate.Expr(ex));
			}
			finally
			{
				ParsingTracer.DecIndentLevel();
			}
		}

		#region ParseCompare

		ISqlPredicate ParseCompare(LambdaInfo lambda, ExpressionType nodeType, ParseInfo left, ParseInfo right, QuerySource[] queries)
		{
			switch (nodeType)
			{
				case ExpressionType.Equal    :
				case ExpressionType.NotEqual :

					var p = ParseObjectComparison(lambda, nodeType, left, right, queries);
					if (p != null)
						return p;

					p = ParseObjectNullComparison(left, right, queries, nodeType == ExpressionType.Equal);
					if (p != null)
						return p;

					p = ParseObjectNullComparison(right, left, queries, nodeType == ExpressionType.Equal);
					if (p != null)
						return p;

					if (left.NodeType == ExpressionType.New || right.NodeType == ExpressionType.New)
						return ParseNewObjectComparison(nodeType, left, right, queries);

					break;
			}

			SqlQuery.Predicate.Operator op;

			switch (nodeType)
			{
				case ExpressionType.Equal             : op = SqlQuery.Predicate.Operator.Equal;          break;
				case ExpressionType.NotEqual          : op = SqlQuery.Predicate.Operator.NotEqual;       break;
				case ExpressionType.GreaterThan       : op = SqlQuery.Predicate.Operator.Greater;        break;
				case ExpressionType.GreaterThanOrEqual: op = SqlQuery.Predicate.Operator.GreaterOrEqual; break;
				case ExpressionType.LessThan          : op = SqlQuery.Predicate.Operator.Less;           break;
				case ExpressionType.LessThanOrEqual   : op = SqlQuery.Predicate.Operator.LessOrEqual;    break;
				default: throw new InvalidOperationException();
			}

			if (left.NodeType == ExpressionType.Convert || right.NodeType == ExpressionType.Convert)
			{
				var p = ParseEnumConversion(left, op, right, queries);
				if (p != null)
					return p;
			}

			var l = ParseExpression(left,  queries);
			var r = ParseExpression(right, queries);

			switch (nodeType)
			{
				case ExpressionType.Equal   :
				case ExpressionType.NotEqual:

					if (!CurrentSql.ParameterDependent && (l is SqlParameter || r is SqlParameter) && l.CanBeNull() && r.CanBeNull())
						CurrentSql.ParameterDependent = true;

					break;
			}

			if (l is SqlQuery.SearchCondition)
				l = Convert(new SqlFunction("CASE", l, new SqlValue(true), new SqlValue(false)));
			//l = Convert(new SqlFunction("CASE",
			//	l, new SqlValue(true),
			//	new SqlQuery.SearchCondition(new[] { new SqlQuery.Condition(true, (SqlQuery.SearchCondition)l) }), new SqlValue(false),
			//	new SqlValue(false)));

			if (r is SqlQuery.SearchCondition)
				r = Convert(new SqlFunction("CASE", r, new SqlValue(true), new SqlValue(false)));
			//r = Convert(new SqlFunction("CASE",
			//	r, new SqlValue(true),
			//	new SqlQuery.SearchCondition(new[] { new SqlQuery.Condition(true, (SqlQuery.SearchCondition)r) }), new SqlValue(false),
			//	new SqlValue(false)));

			return Convert(new SqlQuery.Predicate.ExprExpr(l, op, r));
		}

		#endregion

		#region ParseEnumConversion

		ISqlPredicate ParseEnumConversion(ParseInfo left, SqlQuery.Predicate.Operator op, ParseInfo right, QuerySource[] queries)
		{
			ParseInfo<UnaryExpression> conv;
			ParseInfo                  value;

			if (left.NodeType == ExpressionType.Convert)
			{
				conv  = left.ConvertTo<UnaryExpression>();
				value = right;
			}
			else
			{
				conv  = right.ConvertTo<UnaryExpression>();
				value = left;
			}

			var operand = conv.Create(conv.Expr.Operand, conv.Property(Unary.Operand));
			var type    = operand.Expr.Type;

			if (!type.IsEnum)
				return null;

			var dic = new Dictionary<object, object>();

			var nullValue = _info.MappingSchema.GetNullValue(type);

			if (nullValue != null)
				dic.Add(nullValue, null);

			var mapValues = _info.MappingSchema.GetMapValues(type);

			if (mapValues != null)
				foreach (var mv in mapValues)
					if (!dic.ContainsKey(mv.OrigValue))
						dic.Add(mv.OrigValue, mv.MapValues[0]);

			if (dic.Count == 0)
				return null;

			switch (value.NodeType)
			{
				case ExpressionType.Constant:
					{
						var    origValue = Enum.Parse(type, Enum.GetName(type, ((ConstantExpression)value).Value));
						object mapValue;

						if (!dic.TryGetValue(origValue, out mapValue))
							return null;

						ISqlExpression l, r;

						if (left.NodeType == ExpressionType.Convert)
						{
							l = ParseExpression(operand, queries);
							r = new SqlValue(mapValue);
						}
						else
						{
							r = ParseExpression(operand, queries);
							l = new SqlValue(mapValue);
						}

						return Convert(new SqlQuery.Predicate.ExprExpr(l, op, r));
					}

				case ExpressionType.Convert:
					{
						value = value.ConvertTo<UnaryExpression>();
						value = value.Create(((UnaryExpression)value.Expr).Operand, value.Property(Unary.Operand));

						var l = ParseExpression(operand, queries);
						var r = ParseExpression(value,   queries);

						if (l is SqlParameter) SetParameterEnumConverter((SqlParameter)l, type, _info.MappingSchema);
						if (r is SqlParameter) SetParameterEnumConverter((SqlParameter)r, type, _info.MappingSchema);

						return Convert(new SqlQuery.Predicate.ExprExpr(l, op, r));
					}
			}

			return null;
		}

		static void SetParameterEnumConverter(SqlParameter p, Type type, MappingSchema ms)
		{
			if (p.ValueConverter == null)
			{
				p.ValueConverter = o => ms.MapEnumToValue(o, type);
			}
			else
			{
				var converter = p.ValueConverter;
				p.ValueConverter = o => ms.MapEnumToValue(converter(o), type);
			}
		}

		#endregion

		#region ParseObjectNullComparison

		ISqlPredicate ParseObjectNullComparison(ParseInfo left, ParseInfo right, QuerySource[] queries, bool isEqual)
		{
			if (right.NodeType == ExpressionType.Constant && ((ConstantExpression)right).Value == null)
			{
				if (left.NodeType == ExpressionType.MemberAccess || left.NodeType == ExpressionType.Parameter)
				{
					var field = GetField(left, queries);

					if (field is QuerySource.GroupJoin)
					{
						var join = (QuerySource.GroupJoin)field;
						var expr = join.CheckNullField.GetExpressions(this)[0];

						return Convert(new SqlQuery.Predicate.IsNull(expr, !isEqual));
					}

					if (field is QuerySource || field == null && left.NodeType == ExpressionType.Parameter)
						return new SqlQuery.Predicate.Expr(new SqlValue(!isEqual));
				}
			}

			return null;
		}

		#endregion

		#region ParseObjectComparison

		ISqlPredicate ParseObjectComparison(LambdaInfo lambda, ExpressionType nodeType, ParseInfo left, ParseInfo right, QuerySource[] queries)
		{
			var qsl = GetSource(lambda, left,  queries);
			var qsr = GetSource(lambda, right, queries);

			var sl = qsl as QuerySource.Table;
			var sr = qsr as QuerySource.Table;

			if (qsl != null) for (var query = qsl; sl == null; query = query.BaseQuery)
			{
				sl = query as QuerySource.Table;
				if (!(query is QuerySource.SubQuery))
					break;
			}

			if (qsr != null) for (var query = qsr; sr == null; query = query.BaseQuery)
			{
				sr = query as QuerySource.Table;
				if (!(query is QuerySource.SubQuery))
					break;
			}

			if (sl == null && sr == null)
				return null;

			if (sl == null)
			{
				right = left;
				sl    = sr;
				sr    = null;
				qsl   = qsr;
			}

			var isNull = right.Expr is ConstantExpression && ((ConstantExpression)right.Expr).Value == null;
			var cols   = sl.GetKeyFields();

			var condition = new SqlQuery.SearchCondition();
			var ta        = TypeAccessor.GetAccessor(right.Expr.Type);

			foreach (QueryField.Column col in cols)
			{
				QueryField.Column rcol = null;

				var lcol = col;

				if (sr != null)
				{
					var rfield = sr.SqlTable.Fields[lcol.Field.Name];

					rcol = (QueryField.Column)sr.GetField(rfield);

					if (sr.ParentAssociation != null)
					{
						foreach (var c in sr.ParentAssociationJoin.Condition.Conditions)
						{
							var ee = (SqlQuery.Predicate.ExprExpr)c.Predicate;

							if (ee.Expr2 == rfield)
							{
								rfield = (SqlField)ee.Expr1;
								rcol  = (QueryField.Column)sr.ParentAssociation.GetField(rfield);
								qsr   = sr.ParentAssociation;
								break;
							}
						}
					}

					rcol.Select(this);
				}

				{
					var lfield = sl.SqlTable.Fields[lcol.Field.Name];

					if (sl.ParentAssociation != null)
					{
						foreach (var c in sl.ParentAssociationJoin.Condition.Conditions)
						{
							var ee = (SqlQuery.Predicate.ExprExpr)c.Predicate;

							if (ee.Expr2 == lfield)
							{
								lfield = (SqlField)ee.Expr1;
								lcol   = (QueryField.Column)sl.ParentAssociation.GetField(lfield);
								qsl    = sl.ParentAssociation;
								break;
							}
						}
					}

					lcol.Select(this);
				}

				var rex =
					isNull ?
						new SqlValue(null) :
						sr != null ?
							qsr.GetField(rcol.Field).GetExpressions(this)[0] :
							GetParameter(right, ta[lcol.Field.Name].MemberInfo);

				var predicate = Convert(new SqlQuery.Predicate.ExprExpr(
					qsl.GetField(lcol.Field).GetExpressions(this)[0],
					nodeType == ExpressionType.Equal ? SqlQuery.Predicate.Operator.Equal : SqlQuery.Predicate.Operator.NotEqual,
					rex));

				condition.Conditions.Add(new SqlQuery.Condition(false, predicate));
			}

			if (nodeType == ExpressionType.NotEqual)
				foreach (var c in condition.Conditions)
					c.IsOr = true;

			return condition;
		}

		ISqlPredicate ParseNewObjectComparison(ExpressionType nodeType, ParseInfo left, ParseInfo right, params QuerySource[] queries)
		{
			left  = ConvertExpression(left);
			right = ConvertExpression(right);

			var condition = new SqlQuery.SearchCondition();

			if (left.NodeType != ExpressionType.New)
			{
				var temp = left;
				left  = right;
				right = temp;
			}

			var newExpr  = (NewExpression)left.Expr;
			var newRight = right.Expr as NewExpression;

			for (var i = 0; i < newExpr.Arguments.Count; i++)
			{
				var lex = ParseExpression(left.Create(newExpr.Arguments[i], left.Index(newExpr.Arguments, New.Arguments, i)), queries);
				var rex =
					right.NodeType == ExpressionType.New ?
						ParseExpression(right.Create(newRight.Arguments[i], right.Index(newRight.Arguments, New.Arguments, i)), queries) :
						GetParameter(right, newExpr.Members[i]);

				var predicate = Convert(new SqlQuery.Predicate.ExprExpr(
					lex,
					nodeType == ExpressionType.Equal ? SqlQuery.Predicate.Operator.Equal : SqlQuery.Predicate.Operator.NotEqual,
					rex));

				condition.Conditions.Add(new SqlQuery.Condition(false, predicate));
			}

			if (nodeType == ExpressionType.NotEqual)
				foreach (var c in condition.Conditions)
					c.IsOr = true;

			return condition;
		}

		ISqlExpression GetParameter(ParseInfo pi, MemberInfo member)
		{
			if (member is MethodInfo)
				member = TypeHelper.GetPropertyByMethod((MethodInfo)member);

			var par    = ReplaceParameter(pi, _ => {});
			var expr   = Expression.MakeMemberAccess(par, member);
			var mapper = Expression.Lambda<Func<ExpressionInfo<T>,Expression,object[],object>>(
				Expression.Convert(expr, typeof(object)),
				new [] { _infoParam, _expressionParam, ParametersParam });

			var p = new ExpressionInfo<T>.Parameter
			{
				Expression   = expr,
				Accessor     = mapper.Compile(),
				SqlParameter = new SqlParameter(member.Name, expr.Type, (object)null)
			};

			_parameters.Add(expr, p);
			CurrentSqlParameters.Add(p);

			return p.SqlParameter;
		}

		static ParseInfo ConvertExpression(ParseInfo expr)
		{
			ParseInfo ret = null;

			expr.Walk(pi =>
			{
				if (ret == null) switch (pi.NodeType)
				{
					case ExpressionType.MemberAccess:
					case ExpressionType.New:
						ret = pi;
						pi.StopWalking = true;
						break;
				}

				return pi;
			});

			if (ret == null)
				throw new NotImplementedException();

			return ret;
		}

		#endregion

		#region ParseInPredicate

		private ISqlPredicate ParseInPredicate(ParseInfo pi, params QuerySource[] queries)
		{
			var e    = pi.Expr as MethodCallExpression;
			var expr = ParseExpression(pi.Create(e.Arguments[1], pi.Index(e.Arguments, MethodCall.Arguments, 1)), queries);
			var arr  = pi.Create(e.Arguments[0], pi.Index(e.Arguments, MethodCall.Arguments, 0));

			switch (arr.NodeType)
			{
				case ExpressionType.NewArrayInit:
					{
						var newArr = arr.ConvertTo<NewArrayExpression>();

						if (newArr.Expr.Expressions.Count == 0)
							return new SqlQuery.Predicate.Expr(new SqlValue(false));

						var exprs  = new ISqlExpression[newArr.Expr.Expressions.Count];

						for (var i = 0; i < newArr.Expr.Expressions.Count; i++)
						{
							var item = ParseExpression(
								newArr.Create(newArr.Expr.Expressions[i], newArr.Index(newArr.Expr.Expressions, NewArray.Expressions, i)),
								queries);

							exprs[i] = item;
						}

						return new SqlQuery.Predicate.InList(expr, false, exprs);
					}

				default:
					if (CanBeCompiled(arr))
					{
						var p = BuildParameter(arr).SqlParameter;
						p.IsQueryParameter = false;
						return new SqlQuery.Predicate.InList(expr, false, p);
					}

					break;
			}

			throw new LinqException("'{0}' cannot be converted to SQL.", pi.Expr);
		}

		#endregion

		#region LIKE predicate

		private ISqlPredicate ParseLikePredicate(ParseInfo pi, string start, string end, params QuerySource[] queries)
		{
			var e  = pi.Expr as MethodCallExpression;

			var o = ParseExpression(pi.Create(e.Object,       pi.Property(MethodCall.Object)),                 queries);
			var a = ParseExpression(pi.Create(e.Arguments[0], pi.Index(e.Arguments, MethodCall.Arguments, 0)), queries);

			if (a is SqlValue)
			{
				var value = ((SqlValue)a).Value;

				if (value == null)
					throw new LinqException("NULL cannot be used as a LIKE predicate parameter.");

				return value.ToString().IndexOfAny(new[] { '%', '_' }) < 0?
					new SqlQuery.Predicate.Like(o, false, new SqlValue(start + value + end), null):
					new SqlQuery.Predicate.Like(o, false, new SqlValue(start + EscapeLikeText(value.ToString()) + end), new SqlValue('~'));
			}

			if (a is SqlParameter)
			{
				var p  = (SqlParameter)a;
				var ep = (from pm in CurrentSqlParameters where pm.SqlParameter == p select pm).First();

				ep = new ExpressionInfo<T>.Parameter
				{
					Expression   = ep.Expression,
					Accessor     = ep.Accessor,
					SqlParameter = new SqlParameter(p.Name, ep.Expression.Type, p.Value, GetLikeEscaper(start, end))
				};

				_parameters.Add(e, ep);
				CurrentSqlParameters.Add(ep);

				return new SqlQuery.Predicate.Like(o, false, ep.SqlParameter, new SqlValue('~'));
			}

			return null;
		}

		private ISqlPredicate ParseLikePredicate(ParseInfo pi, params QuerySource[] queries)
		{
			var e  = pi.Expr as MethodCallExpression;
			var a1 = ParseExpression(pi.Create(e.Arguments[0], pi.Index(e.Arguments, MethodCall.Arguments, 0)), queries);
			var a2 = ParseExpression(pi.Create(e.Arguments[1], pi.Index(e.Arguments, MethodCall.Arguments, 1)), queries);

			ISqlExpression a3 = null;

			if (e.Arguments.Count == 3)
				a3 = ParseExpression(pi.Create(e.Arguments[2], pi.Index(e.Arguments, MethodCall.Arguments, 2)), queries);

			return new SqlQuery.Predicate.Like(a1, false, a2, a3);
		}

		static string EscapeLikeText(string text)
		{
			if (text.IndexOfAny(new[] { '%', '_' }) < 0)
				return text;

			var builder = new StringBuilder(text.Length);

			foreach (var ch in text)
			{
				switch (ch)
				{
					case '%':
					case '_':
					case '~':
						builder.Append('~');
						break;
				}

				builder.Append(ch);
			}

			return builder.ToString();
		}

		static Converter<object,object> GetLikeEscaper(string start, string end)
		{
			return value => value == null? null: start + EscapeLikeText(value.ToString()) + end;
		}

		#endregion

		#region MakeIsPredicate

		ISqlPredicate MakeIsPredicate(QuerySource.Table table, Type typeOperand)
		{
			if (typeOperand == table.ObjectType && table.InheritanceMapping.Count(m => m.Type == typeOperand) == 0)
				return Convert(new SqlQuery.Predicate.Expr(new SqlValue(true)));

			var mapping = table.InheritanceMapping.Select((m,i) => new { m, i }).Where(m => m.m.Type == typeOperand && !m.m.IsDefault).ToList();

			switch (mapping.Count)
			{
				case 0:
					{
						var cond = new SqlQuery.SearchCondition();

						foreach (var m in table.InheritanceMapping.Select((m,i) => new { m, i }).Where(m => !m.m.IsDefault))
						{
							cond.Conditions.Add(
								new SqlQuery.Condition(
									false, 
									Convert(new SqlQuery.Predicate.ExprExpr(
										table.Columns[table.InheritanceDiscriminators[m.i]].Field,
										SqlQuery.Predicate.Operator.NotEqual,
										new SqlValue(m.m.Code)))));
						}

						return cond;
					}

				case 1:
					return Convert(new SqlQuery.Predicate.ExprExpr(
						table.Columns[table.InheritanceDiscriminators[mapping[0].i]].Field,
						SqlQuery.Predicate.Operator.Equal,
						new SqlValue(mapping[0].m.Code)));

				default:
					{
						var cond = new SqlQuery.SearchCondition();

						foreach (var m in mapping)
						{
							cond.Conditions.Add(
								new SqlQuery.Condition(
									false,
									Convert(new SqlQuery.Predicate.ExprExpr(
										table.Columns[table.InheritanceDiscriminators[m.i]].Field,
										SqlQuery.Predicate.Operator.Equal,
										new SqlValue(m.m.Code))),
									true));
						}

						return cond;
					}
			}
		}

		#endregion

		#endregion

		#region Search Condition Parser

		void ParseSearchCondition(ICollection<SqlQuery.Condition> conditions, LambdaInfo lambda, ParseInfo parseInfo, params QuerySource[] queries)
		{
			ParsingTracer.WriteLine(lambda);
			ParsingTracer.WriteLine(parseInfo);
			ParsingTracer.WriteLine(queries);
			ParsingTracer.IncIndentLevel();

			switch (parseInfo.NodeType)
			{
				case ExpressionType.AndAlso:
					{
						var pi = parseInfo.Convert<BinaryExpression>();
						var e  = parseInfo.Expr as BinaryExpression;

						ParseSearchCondition(conditions, lambda, pi.Create(e.Left,  pi.Property(Binary.Left)),  queries);
						ParseSearchCondition(conditions, lambda, pi.Create(e.Right, pi.Property(Binary.Right)), queries);

						break;
					}

				case ExpressionType.OrElse:
					{
						var pi = parseInfo.Convert<BinaryExpression>();
						var e  = parseInfo.Expr as BinaryExpression;

						var orCondition = new SqlQuery.SearchCondition();

						ParseSearchCondition(orCondition.Conditions, lambda, pi.Create(e.Left,  pi.Property(Binary.Left)),  queries);
						orCondition.Conditions[orCondition.Conditions.Count - 1].IsOr = true;
						ParseSearchCondition(orCondition.Conditions, lambda, pi.Create(e.Right, pi.Property(Binary.Right)), queries);

						conditions.Add(new SqlQuery.Condition(false, orCondition));

						break;
					}

				case ExpressionType.Not:
					{
						var pi = parseInfo.Convert<UnaryExpression>();
						var e  = parseInfo.Expr as UnaryExpression;

						var notCondition = new SqlQuery.SearchCondition();

						ParseSearchCondition(notCondition.Conditions, lambda, pi.Create(e.Operand, pi.Property(Unary.Operand)), queries);

						if (notCondition.Conditions.Count == 1 && notCondition.Conditions[0].Predicate is SqlQuery.Predicate.NotExpr)
						{
							var p = notCondition.Conditions[0].Predicate as SqlQuery.Predicate.NotExpr;
							p.IsNot = !p.IsNot;
							conditions.Add(notCondition.Conditions[0]);
						}
						else
							conditions.Add(new SqlQuery.Condition(true, notCondition));

						break;
					}

				default:
					var predicate = ParsePredicate(lambda, parseInfo, queries);
					conditions.Add(new SqlQuery.Condition(false, predicate));
					break;
			}

			ParsingTracer.DecIndentLevel();
		}

		#endregion

		#region ParentQueries

		class ParentQuery
		{
			public QuerySource                    Parent;
			public ParseInfo<ParameterExpression> Parameter;
		}

		readonly List<ParentQuery> _parentQueries = new List<ParentQuery>();

		#endregion

		#region Helpers

		QuerySource.Table CreateTable(SqlQuery sqlQuery, LambdaInfo lambda)
		{
			var table = new QuerySource.Table(_info.MappingSchema, sqlQuery, lambda);

			if (table.ObjectType != table.OriginalType)
			{
				var predicate = MakeIsPredicate(table, table.OriginalType);

				if (predicate.GetType() != typeof(SqlQuery.Predicate.Expr))
					CurrentSql.Where.SearchCondition.Conditions.Add(new SqlQuery.Condition(false, predicate));
			}

			return table;
		}

		QuerySource.Table CreateTable(SqlQuery sqlQuery, Type type)
		{
			var table = new QuerySource.Table(_info.MappingSchema, sqlQuery, type);

			if (table.ObjectType != table.OriginalType)
			{
				var predicate = MakeIsPredicate(table, table.OriginalType);

				if (predicate.GetType() != typeof(SqlQuery.Predicate.Expr))
					CurrentSql.Where.SearchCondition.Conditions.Add(new SqlQuery.Condition(false, predicate));
			}

			return table;
		}

		QueryField GetField(Expression expr, params QuerySource[] queries)
		{
			foreach (var query in queries)
			{
				var field = query.GetField(expr);

				if (field != null)
					return field;
			}

			foreach (var query in _parentQueries)
			{
				var field = query.Parent.GetField(expr);

				if (field != null)
					return field;
			}

			return null;
		}

		QuerySource GetSource(LambdaInfo lambda, Expression expr, params QuerySource[] queries)
		{
			switch (expr.NodeType)
			{
				case ExpressionType.Parameter:
					if (lambda != null)
					{
						for (var i = 0; i < lambda.Parameters.Length; i++)
						{
							var p = lambda.Parameters[i];
							if (p.Expr == expr)
								return queries[i];
						}
					}

					break;

				case ExpressionType.MemberAccess:
					{
						var ma = (MemberExpression)expr;

						if (lambda != null && ma.Expression == lambda.Parameters[0].Expr)
						{
							foreach (var query in queries)
							{
								var gb = query as QuerySource.GroupBy;
								if (gb != null && gb.BaseQuery.ObjectType == expr.Type)
									return gb.BaseQuery;
							}
						}
					}

					break;
			}

			foreach (var query in queries)
			{
				var field = query.GetField(expr);

				if (field != null)
				{
					if (field is QuerySource)
						return (QuerySource)field;

					var sq = field as QueryField.SubQueryColumn;

					if (sq != null)
						return sq.Field as QuerySource;

					return null;
				}
			}

			foreach (var query in _parentQueries)
			{
				var field = query.Parent.GetField(expr) as QuerySource;

				if (field != null)
					return field;
			}

			return null;
		}

		static QuerySource[] Concat(QuerySource[] q1, QuerySource[] q2)
		{
			if (q2 == null || q2.Length == 0) return q1;
			if (q1 == null || q1.Length == 0) return q2;

			return q1.Concat(q2).ToArray();
		}

		static QuerySource[] Concat(QuerySource[] q1, ICollection<ParentQuery> q2)
		{
			if (q2 == null || q2.Count == 0) return q1;
			return Concat(q1, q2.Select(q => q.Parent).ToArray());
		}

		bool HasSource(QuerySource query, QuerySource source)
		{
			if (source == null)  return false;
			if (source == query) return true;

			foreach (var s in query.Sources)
				if (HasSource(s, source))
					return true;

			return false;
		}

		static void SetAlias(QuerySource query, string alias)
		{
			if (alias.Contains('<'))
				return;

			query.Match
			(
				table  =>
				{
					if (table.SqlTable.Alias == null)
						table.SqlTable.Alias = alias;
				},
				_ => {},
				subQuery =>
				{
					var table = subQuery.SqlQuery.From.Tables[0];
					if (table.Alias == null)
						table.Alias = alias;
				},
				_ => {},
				_ => {}
			);
		}

		QuerySource.SubQuery WrapInSubQuery(QuerySource source)
		{
			var result = new QuerySource.SubQuery(new SqlQuery(), source.SqlQuery, source);
			CurrentSql = result.SqlQuery;
			return result;
		}

		SqlFunctionAttribute GetFunctionAttribute(ICustomAttributeProvider member)
		{
			var attrs = member.GetCustomAttributes(typeof(SqlFunctionAttribute), true);

			if (attrs.Length == 0)
				return null;

			SqlFunctionAttribute attr = null;

			foreach (SqlFunctionAttribute a in attrs)
			{
				if (a.SqlProvider == _info.SqlProvider.Name)
				{
					attr = a;
					break;
				}

				if (a.SqlProvider == null)
					attr = a;
			}

			return attr;
		}

		public ISqlExpression Convert(ISqlExpression expr)
		{
			_info.SqlProvider.SqlQuery = CurrentSql;
			return _info.SqlProvider.ConvertExpression(expr);
		}

		public ISqlPredicate Convert(ISqlPredicate predicate)
		{
			_info.SqlProvider.SqlQuery = CurrentSql;
			return _info.SqlProvider.ConvertPredicate(predicate);
		}

		static bool IsNullableValueMember(MemberInfo member)
		{
			return
				member.Name == "Value" &&
				member.DeclaringType.IsGenericType &&
				member.DeclaringType.GetGenericTypeDefinition() == typeof(Nullable<>);
		}

		static bool IsListCountMember(MemberInfo member)
		{
			if (member.Name == "Count")
			{
				if (member.DeclaringType.IsSubclassOf(typeof(CollectionBase)))
					return true;

				foreach (var t in member.DeclaringType.GetInterfaces())
					if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IList<>))
						return true;
			}

			return false;
		}

		#endregion
	}
}
