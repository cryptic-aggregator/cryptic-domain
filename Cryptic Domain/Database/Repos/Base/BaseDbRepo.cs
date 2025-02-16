using System.Linq.Expressions;
using System.Reflection;
using Cryptic_Domain.Database.Attributes;
using Cryptic_Domain.Database.Config.Interfaces;
using Cryptic_Domain.Database.Interfaces;
using Cryptic_Domain.Exeptions;
using Cryptic_Domain.Helpers;
using Cryptic_Domain.Interfaces;
using Cryptic_Domain.Interfaces.Database;
using Cryptic_Domain.Interfaces.Database.Base;
using Npgsql;

namespace Cryptic_Domain.Database.Repos.Base;

public abstract class BaseDbRepo<T> : IBaseDbRepo<T>, IDisposable where T : IDatabaseTable
    {
        protected BaseDbRepo(IDatabaseConnectionService dbConnectionService,
            IDatabaseConfiguration dbConfiguration)
        {
            _tableType = typeof(T);
            _schema = dbConfiguration.Schema;
            _columnNames = GetAllColumnNames();
            _tableName = GetTableName();
            _searchPath = $"{_schema}.\"{_tableName}\"";

            _dbConnectionService = dbConnectionService;

            _conn = GetConnection();
            _conn.Open();
        }

        public string FullTablePath => _searchPath;
        protected NpgsqlConnection Connection => _conn;
        protected string Schema => _schema;
        protected string[] Columns => _columnNames;
        protected string TableName => _tableName;

        private NpgsqlConnection GetConnection()
        {
            return _dbConnectionService.GetDbConnection();
        }


        public virtual async Task<T?> GetByIdAsync(int id)
        {
            var command = $"SELECT {string.Join(", ", Columns)} FROM {FullTablePath} WHERE id = @Id";
            using var cmd = new NpgsqlCommand(command, Connection);
            cmd.Parameters.AddWithValue("Id", id);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return await reader.MapAsync<T>();
            }

            return default;
        }


        public virtual async Task<T?> GetByIdAsync(Guid id)
        {
            var command = $"SELECT {string.Join(", ", Columns)} FROM {FullTablePath} WHERE id = @Id";
            using var cmd = new NpgsqlCommand(command, Connection);
            cmd.Parameters.AddWithValue("Id", NpgsqlTypes.NpgsqlDbType.Uuid, id);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return await reader.MapAsync<T>();
            }

            return default;
        }
        
        public virtual async Task<List<T>> GetByIdsAsync(Guid[] ids)
        {
            var command = $"SELECT {string.Join(", ", Columns)} FROM {FullTablePath} WHERE id IN ({string.Join(',', ids.Select(x => $"'{x}'"))})";
            using var cmd = new NpgsqlCommand(command, Connection);

            using var reader = await cmd.ExecuteReaderAsync();
            var mapTasks = new List<Task<T>>();
            while (await reader.ReadAsync())
            {
                mapTasks.Add(reader.MapAsync<T>());
            }
            await Task.WhenAll(mapTasks);

            return mapTasks.Select(x => x.GetAwaiter().GetResult()).ToList();
        }
        
        public virtual async Task<List<T>> GetByIdsAsync(int[] ids)
        {
            if (ids == null || ids.Length == 0)
                return new List<T>();

            var command = $"SELECT {string.Join(", ", Columns)} FROM {FullTablePath} WHERE id = ANY(@ids)";
            using var cmd = new NpgsqlCommand(command, Connection);
            cmd.Parameters.AddWithValue("ids", ids);

            using var reader = await cmd.ExecuteReaderAsync();
            var mapTasks = new List<Task<T>>();
            while (await reader.ReadAsync())
            {
                mapTasks.Add(reader.MapAsync<T>());
            }
            await Task.WhenAll(mapTasks);

            return mapTasks.Select(x => x.GetAwaiter().GetResult()).ToList();
        }
        
        public virtual async Task<T?> GetByIdAsync(Guid id, int userId)
        {
            var command = $"SELECT {string.Join(", ", Columns)} FROM {FullTablePath} WHERE id = @Id";

            if (userId != -1) //TODO Thats HACK for b2b
            {
                command += " AND owner_id = @OwnerId";
            }

            using var cmd = new NpgsqlCommand(command, Connection);
            cmd.Parameters.AddWithValue("Id", id);

            if (userId != -1)
            {
                cmd.Parameters.AddWithValue("OwnerId", userId);
            }

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return await reader.MapAsync<T>();
            }

            return default;
        }
        
        public virtual async Task DeleteAsync(Guid id)
        {
            using var cmd = new NpgsqlCommand($"DELETE FROM {FullTablePath} WHERE id = @Id", Connection);
            cmd.Parameters.AddWithValue("Id", NpgsqlTypes.NpgsqlDbType.Uuid, id);
            if (await cmd.ExecuteNonQueryAsync() == 0)
                throw new InvalidDbOperations("Couldn't delete any item");
        }
        
        public virtual async Task DeleteAsync(int id)
        {
            using var cmd = new NpgsqlCommand($"DELETE FROM {FullTablePath} WHERE id = @Id", Connection);
            cmd.Parameters.AddWithValue("Id", id);
            if (await cmd.ExecuteNonQueryAsync() == 0)
                throw new InvalidDbOperations("Couldn't delete any item");
        }

        protected List<string> GetPropertyNamesToUpdate(Expression<Func<T, object>> selector)
        {
            if (selector.Body is NewExpression memberInit)
            {
                if (memberInit == null)
                {
                    throw new ArgumentException("Selector must be a new expression.");
                }

                var properties = memberInit.Members.Select((member, index) =>
                {
                    return member.Name;
                }).ToList();

                return properties;
            }
            else if (selector.Body is UnaryExpression unaryExpression &&
                unaryExpression.Operand is MemberExpression operandMemberExpression)
            {
                return new List<string>() { operandMemberExpression.Member.Name };
            }
            else if (selector.Body is MemberExpression memberExpression)
            {
                return new List<string> { memberExpression.Member.Name };
            }
            else
            {
                throw new ArgumentException("not expected expression type, allowed only New, Unary, and Member");
            }
        }

        protected (string SetterString, List<NpgsqlParameter> Parameters) GetUpdateParameters(T entity, List<string> propertyNamesToUpdate)
        {
            var paramSetters = new List<string>();
            var parameters = new List<NpgsqlParameter>();

            var typeProps = typeof(T).GetProperties();
            for (int i = 0; i < propertyNamesToUpdate.Count; i++)
            {
                var typeProp = typeProps.FirstOrDefault(x => x.Name == propertyNamesToUpdate[i]);
                if (typeProp != null)
                {
                    var columnName = typeProp.Name;
                    var columnData = System.Attribute.GetCustomAttribute(typeProp, typeof(ColumnAttribute)) as ColumnAttribute;

                    if (columnData != null)
                    {
                        columnName = columnData.Name;
                        object value = null;
                        if (typeProp.PropertyType.IsEnum)
                        {
                            value = Convert.ChangeType(typeProp.GetValue(entity), typeof(int));
                        }
                        else
                        {
                            value = typeProp.GetValue(entity);
                        }
                        paramSetters.Add($"{columnName} = @Param{i}");
                        var param = new NpgsqlParameter($"Param{i}", columnData.DataType);

                        if (value == null)
                        {
                            param.Value = DBNull.Value;
                        }
                        else
                        {
                            param.Value = value;
                        }
                        parameters.Add(param);
                    }
                }
                else
                {
                    throw new InvalidOperationException($"Some errors occurred with selector, property {propertyNamesToUpdate[i]} cannot be found");
                }
            }

            var paramSetter = string.Join(',', paramSetters);
            return (paramSetter, parameters);
        }

        public void Dispose()
        {
            if (_conn.State != System.Data.ConnectionState.Closed)
            {
                _conn.Close();
            }

            _conn.Dispose();
        }

        private string[] GetAllColumnNames()
        {
            if (CachedColumnNames.TryGetValue(_tableType, out var columnNamesCached))
            {
                return columnNamesCached;
            }

            lock (_lock)
            {
                if (CachedColumnNames.TryGetValue(_tableType, out columnNamesCached))
                {
                    return columnNamesCached;
                }

                var typeProps = typeof(T).GetProperties()
                    .Where(x => x.CustomAttributes.Select(x => x.AttributeType).Contains(typeof(ColumnAttribute)));

                var columnNames = new List<string>();
                foreach (var typeProp in typeProps)
                {
                    var columnData = Attribute.GetCustomAttribute(typeProp, typeof(ColumnAttribute)) as ColumnAttribute;
                    if (columnData != null)
                    {
                        columnNames.Add(columnData.Name);
                    }
                }

                var columnNamesArray = columnNames.ToArray();
                CachedColumnNames[_tableType] = columnNamesArray; // Use indexer to avoid duplicate key

                return columnNamesArray;
            }
        }

        protected string GetTableName<T1>() where T1 : IDatabaseTable
        {
            var type = typeof(T1);
            
            if (CachedTableNames.TryGetValue(type, out var tableName))
            {
                return tableName;
            }

            lock (_lock)
            {
                if (CachedTableNames.TryGetValue(type, out tableName))
                {
                    return tableName;
                }

                var tableInfo = type.GetCustomAttribute(typeof(TableAttribute)) as TableAttribute;
                if (tableInfo != null)
                {
                    tableName = tableInfo.TableName;
                    CachedTableNames[type] = tableName; // Use indexer instead of Add to avoid duplicate key error
                    return tableName;
                }
                else
                {
                    throw new InvalidOperationException("Try to get table name not from table or no TableAttribute provided");
                }
            }
        }

        private string GetTableName()
        {
            return GetTableName<T>();
        }

        private readonly string _schema;
        private readonly string _searchPath;
        private readonly string _tableName;
        private readonly string[] _columnNames;
        private readonly NpgsqlConnection _conn;
        private readonly Type _tableType;

        private static readonly object _lock = new object();

        private static readonly Dictionary<Type, string[]> CachedColumnNames = new Dictionary<Type, string[]>();
        private static readonly Dictionary<Type, string> CachedTableNames = new Dictionary<Type, string>();

        private readonly IDatabaseConnectionService _dbConnectionService;
    }