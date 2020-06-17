using System;
using System.Collections.Generic;
using System.Data;
using System.Reflection;
using System.Text;
using Dapper;

namespace BlazorAdmin.Service.Util
{
    public class DBUtils
    {
        //public static Tuple<string, DynamicParameters> GetUpdateSqlAndParameters<T>(T param)
        //{
        //    var result = new DynamicParameters();
        //    var strlist = new List<string>();
        //    var pType = typeof(T);
        //    var customAttributeData = pType.CustomAttributes.FirstOrDefault(x => x.AttributeType == typeof(TableAttribute));
        //    var tablename = customAttributeData?.ConstructorArguments[0].Value.ToString();

        //    if (tablename == null)
        //    {
        //        throw new ArgumentNullException(nameof(tablename), "table name is null");
        //    }

        //    var propertyInfos = pType.GetProperties();
        //    foreach (var fieldInfo in propertyInfos)
        //    {
        //        if (fieldInfo.Name == "Id")
        //        {
        //            result.Add("@Id", fieldInfo.GetValue(param), DbType.Guid, ParameterDirection.Input);
        //            continue;
        //        }
        //        if (fieldInfo.Name == "OrganizationId")
        //        {
        //            result.Add("@OrganizationId", fieldInfo.GetValue(param), DbType.Guid, ParameterDirection.Input);
        //            continue;
        //        }
        //        //if (!fieldInfo.PropertyType.IsPrimitive) continue;
        //        foreach (var fieldInfoCustomAttribute in fieldInfo.CustomAttributes)
        //        {
        //            if (fieldInfoCustomAttribute.AttributeType != typeof(NeedUpdateAttribute)) continue;
        //            var fieldvalue = fieldInfo.GetValue(param);
        //            if (Convert.ToBoolean(fieldInfoCustomAttribute.ConstructorArguments[0].Value) &&
        //               IsDefaultValue(fieldInfo, fieldvalue))
        //            {
        //                break;
        //            }
        //            strlist.Add(fieldInfo.Name);
        //            result.Add("@" + fieldInfo.Name, fieldvalue, Convert2DbType(fieldInfo.PropertyType), ParameterDirection.Input);
        //            break;
        //        }
        //    }
        //    StringBuilder sb = new StringBuilder();
        //    sb.AppendLine(" update ");
        //    sb.AppendLine(tablename);
        //    sb.AppendLine(" set ");
        //    sb.AppendJoin(',', strlist.Select(x => $" {x}=@{x} "));
        //    sb.AppendLine("where Id=@Id");
        //    return new Tuple<string, DynamicParameters>(sb.ToString(), result); ;
        //}

        public static (string sql, string totalcountsql, DynamicParameters parameter) GetPaginationSqlAndParameters(
            string tableName, int pageNo, int pageSize, object param,
            (string fieldName, DateTime timeBegin, DateTime timeEnd)[] timeTuples = null, bool checkDr = true, string paginationOrderField = null, bool isDesc = false)
        {
            paginationOrderField ??= "addtime";

            var totalcountsql = $"select count({paginationOrderField}) from {tableName} ";

            var parameter = new DynamicParameters();
            var sb = new StringBuilder();
            sb.AppendLine($" select * from {tableName} ");

            var propertyInfos = param.GetType().GetProperties();

            var strlist = new List<string>(propertyInfos.Length);

            foreach (var fieldInfo in propertyInfos)
            {
                var fieldvalue = fieldInfo.GetValue(param);
                if (IsDefaultValue(fieldInfo, fieldvalue))
                {
                    continue;
                }

                if (fieldInfo.PropertyType == typeof(string))
                {
                    if (!string.IsNullOrWhiteSpace(fieldvalue as string))
                    {
                        strlist.Add($" {fieldInfo.Name} like @{fieldInfo.Name} ");
                        parameter.Add("@" + fieldInfo.Name, $"%{fieldvalue}%", DbType.String, ParameterDirection.Input);
                    }
                }
                else
                {
                    strlist.Add($" {fieldInfo.Name} = @{fieldInfo.Name} ");
                    parameter.Add("@" + fieldInfo.Name, fieldvalue, Convert2DbType(fieldInfo.PropertyType), ParameterDirection.Input);
                }
            }

            if (timeTuples != null)
            {
                List<string> timeFilter = new List<string>(timeTuples.Length);
                foreach (var (fieldName, timeBegin, timeEnd) in timeTuples)
                {
                    string str = "";
                    if (timeBegin != default)
                    {
                        str = ($" {fieldName} >= @{fieldName}begin ");
                        parameter.Add($"@{fieldName}begin", timeBegin, DbType.DateTime, ParameterDirection.Input);

                    }

                    if (timeEnd != default && timeEnd != DateTime.MaxValue)
                    {
                        if (str.Length > 0)
                        {
                            str += "and";
                        }
                        str += ($" {fieldName} <= @{fieldName}end");
                        parameter.Add($"@{fieldName}end", timeEnd, DbType.DateTime, ParameterDirection.Input);
                    }

                    if (str.Length > 0)
                    {
                        timeFilter.Add($" ({str}) ");
                    }

                }

                if (timeFilter.Count > 0)
                {
                    strlist.Add($"({string.Join(" or ", timeFilter)})");
                }
            }
            if (checkDr)
            {

                sb.AppendLine(" where dr=0 ");

                if (strlist.Count > 0)
                {
                    sb.Append(" and ");

                    sb.AppendJoin(" and ", strlist);

                    totalcountsql = $"{totalcountsql} where dr=0 and {string.Join(" and ", strlist)}";
                }
                else
                {
                    totalcountsql = $"{totalcountsql} where dr=0";
                }
            }
            else
            {
                if (strlist.Count > 0)
                {
                    sb.Append(" where ");
                    sb.AppendJoin(" and ", strlist);

                    totalcountsql = $"{totalcountsql} where {string.Join(" and ", strlist)}";
                }
            }

            sb.AppendLine($" order by {paginationOrderField} {(isDesc ? " DESC" : "")} limit @pagestartindex,@pagesize ");
            parameter.Add("@pagestartindex", (pageNo - 1) * pageSize, DbType.Int32, ParameterDirection.Input);
            parameter.Add("@pagesize", pageSize, DbType.Int32, ParameterDirection.Input);

            var sql = sb.ToString();

            return (sql, totalcountsql, parameter);
        }

        public static (string sql, string totalCountSql, DynamicParameters parameter) GetPaginationSqlAndParametersUseViewSql(
               string tableName, int pageNo, int pageSize, object param,
               (string fieldName, DateTime timeBegin, DateTime timeEnd)[] timeTuples = null, bool checkDr = true, string paginationOrderField = null, bool isDesc = false)
        {
            paginationOrderField ??= "addtime";

            var totalCountSql = $"select count({paginationOrderField}) from ({tableName}) origin";

            var parameter = new DynamicParameters();
            var sb = new StringBuilder();
            sb.AppendLine($" select * from ({tableName}) origin");

            var propertyInfos = param.GetType().GetProperties();

            var filters = new List<string>(propertyInfos.Length);

            foreach (var fieldInfo in propertyInfos)
            {
                var value = fieldInfo.GetValue(param);
                if (IsDefaultValue(fieldInfo, value))
                {
                    continue;
                }

                if (fieldInfo.PropertyType == typeof(string))
                {
                    if (!string.IsNullOrWhiteSpace(value as string))
                    {
                        filters.Add($" {fieldInfo.Name} like @{fieldInfo.Name} ");
                        parameter.Add("@" + fieldInfo.Name, $"%{value}%", DbType.String, ParameterDirection.Input);
                    }
                }
                else
                {
                    filters.Add($" {fieldInfo.Name} = @{fieldInfo.Name} ");
                    parameter.Add("@" + fieldInfo.Name, value, Convert2DbType(fieldInfo.PropertyType), ParameterDirection.Input);
                }
            }

            if (timeTuples != null)
            {
                List<string> timeFilter = new List<string>(timeTuples.Length);
                foreach (var (fieldName, timeBegin, timeEnd) in timeTuples)
                {
                    string str = "";
                    if (timeBegin != default)
                    {
                        str = ($" {fieldName} >= @{fieldName}begin ");
                        parameter.Add($"@{fieldName}begin", timeBegin, DbType.DateTime, ParameterDirection.Input);

                    }

                    if (timeEnd != default && timeEnd != DateTime.MaxValue)
                    {
                        if (str.Length > 0)
                        {
                            str += "and";
                        }
                        str += ($" {fieldName} <= @{fieldName}end");
                        parameter.Add($"@{fieldName}end", timeEnd, DbType.DateTime, ParameterDirection.Input);
                    }

                    if (str.Length > 0)
                    {
                        timeFilter.Add($" ({str}) ");
                    }

                }

                if (timeFilter.Count > 0)
                {
                    filters.Add($"({string.Join(" or ", timeFilter)})");
                }
            }
            if (checkDr)
            {

                sb.AppendLine(" where dr=0 ");

                if (filters.Count > 0)
                {
                    sb.Append(" and ");

                    sb.AppendJoin(" and ", filters);

                    totalCountSql = $"{totalCountSql} where dr=0 and {string.Join(" and ", filters)}";
                }
                else
                {
                    totalCountSql = $"{totalCountSql} where dr=0";
                }
            }
            else
            {
                if (filters.Count > 0)
                {
                    sb.Append(" where ");
                    sb.AppendJoin(" and ", filters);

                    totalCountSql = $"{totalCountSql} where {string.Join(" and ", filters)}";
                }
            }

            sb.AppendLine($" order by {paginationOrderField} {(isDesc ? " DESC" : "")} limit @pagestartindex,@pagesize ");
            parameter.Add("@pagestartindex", (pageNo - 1) * pageSize, DbType.Int32, ParameterDirection.Input);
            parameter.Add("@pagesize", pageSize, DbType.Int32, ParameterDirection.Input);

            var sql = sb.ToString();

            return (sql, totalCountSql, parameter);
        }


        private static bool IsDefaultValue(PropertyInfo fieldInfo, object fieldvalue)
        {
            if (!fieldInfo.PropertyType.IsValueType)
                return fieldvalue == null;
            if (fieldInfo.PropertyType.IsGenericType && fieldInfo.PropertyType.
                    GetGenericTypeDefinition() == typeof(Nullable<>))
                return fieldvalue == null;
            var t = fieldInfo.PropertyType;
            while (true)
            {
                if (t == typeof(DateTime))
                {
                    return (DateTime)fieldvalue == default;
                }
                if (t == typeof(Int32))
                {
                    return (Int32)fieldvalue == default;
                }
                if (t == typeof(Int64))
                {
                    return (Int64)fieldvalue == default;
                }
                if (t == typeof(Guid))
                {
                    return (Guid)fieldvalue == default;
                }
                if (t == typeof(Decimal))
                {
                    return (Decimal)fieldvalue == default;
                }
                if (t == typeof(byte))
                {
                    return (byte)fieldvalue == default;
                }
                if (t == typeof(bool))
                {
                    return (bool)fieldvalue == default;
                }
                if (t == typeof(DateTime))
                {
                    return (DateTime)fieldvalue == default;
                }
                if (t == typeof(char))
                {
                    return (char)fieldvalue == default;
                }
                if (t.IsEnum)
                {
                    var enumUnderlyingType = t.GetEnumUnderlyingType();
                    t = enumUnderlyingType;
                }
            }

        }

        private static DbType Convert2DbType(Type t)
        {
            while (true)
            {
                if (t == typeof(string))
                {
                    return DbType.String;
                }

                if (t == typeof(DateTime))
                {
                    return DbType.DateTime;
                }

                if (t == typeof(Int32))
                {
                    return DbType.Int32;
                }

                if (t == typeof(Int64))
                {
                    return DbType.Int64;
                }

                if (t == typeof(Guid))
                {
                    return DbType.Guid;
                }

                if (t == typeof(Decimal))
                {
                    return DbType.Decimal;
                }
                if (t == typeof(byte))
                {
                    return DbType.Byte;
                }
                if (t == typeof(bool))
                {
                    return DbType.Boolean;
                }

                if (t == typeof(char))
                {
                    return DbType.Byte;
                }
                if (t.IsEnum)
                {
                    var enumUnderlyingType = t.GetEnumUnderlyingType();
                    t = enumUnderlyingType;
                    continue;
                }

                if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Nullable<>))
                {
                    t = t.GetGenericArguments()[0];
                    continue;
                }

                throw new ArgumentException("unsupport type");
            }
        }
    }
}