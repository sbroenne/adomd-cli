using System.Data;

public class RowsetResultTests
{
    [Fact]
    public void DataTableToRows_NotTruncated_WhenRowCountAtOrBelowLimit()
    {
        var table = CreateTable(3);

        var result = AnalysisServices.DataTableToRows(table, limit: 5);

        Assert.Equal(3, result.RowCount);
        Assert.False(result.Truncated);
    }

    [Fact]
    public void DataTableToRows_Truncated_WhenRowCountExceedsLimit()
    {
        var table = CreateTable(5);

        var result = AnalysisServices.DataTableToRows(table, limit: 2);

        Assert.Equal(2, result.RowCount);
        Assert.True(result.Truncated);
    }

    [Fact]
    public void DataTableToRows_MapsDbNullToNull()
    {
        var table = new DataTable();
        table.Columns.Add("Name", typeof(string));
        var row = table.NewRow();
        row["Name"] = DBNull.Value;
        table.Rows.Add(row);

        var result = AnalysisServices.DataTableToRows(table, limit: 10);

        Assert.Null(result.Rows[0]["Name"]);
    }

    [Fact]
    public void ToJson_ExposesRowCountAndTruncatedFlag()
    {
        var result = new RowsetResult
        {
            Rows = [new Dictionary<string, object?> { ["a"] = 1 }],
            Truncated = true
        };

        dynamic json = result.ToJson();

        Assert.Equal(1, json.rowCount);
        Assert.True(json.truncated);
    }

    [Fact]
    public void Error_CarriesExceptionDetailsAndFlagsIsError()
    {
        var result = RowsetResult.Error(new InvalidOperationException("boom"));

        Assert.False(result.Truncated);
        Assert.Empty(result.Rows);
        Assert.True(result.IsError);
        Assert.Equal("boom", result.ErrorMessage);
        Assert.Equal(typeof(InvalidOperationException).FullName, result.ErrorType);
    }

    [Fact]
    public void ToJson_IncludesErrorFields_WhenErrored()
    {
        dynamic json = RowsetResult.Error(new InvalidOperationException("boom")).ToJson();

        Assert.Equal("boom", json.error);
        Assert.Equal(typeof(InvalidOperationException).FullName, json.exception);
    }

    [Fact]
    public void IsError_False_ForNormalResult()
    {
        var result = new RowsetResult { Rows = [], Truncated = false };

        Assert.False(result.IsError);
        Assert.Null(result.ErrorMessage);
    }

    private static DataTable CreateTable(int rowCount)
    {
        var table = new DataTable();
        table.Columns.Add("Id", typeof(int));
        for (var i = 0; i < rowCount; i++)
        {
            var row = table.NewRow();
            row["Id"] = i;
            table.Rows.Add(row);
        }

        return table;
    }
}
