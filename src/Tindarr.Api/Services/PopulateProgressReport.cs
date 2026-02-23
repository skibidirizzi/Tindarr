using Tindarr.Application.Abstractions.Ops;
using Tindarr.Contracts.Admin;

namespace Tindarr.Api.Services;

public sealed class PopulateProgressReport : IPopulateProgressReport
{
	private readonly object _gate = new();
	private string _state = "idle";
	private int _detailsTotal;
	private int _detailsDone;
	private int _imagesTotal;
	private int _imagesDone;
	private string? _lastMessage;

	public PopulateStatusDto GetStatus()
	{
		lock (_gate)
		{
			return new PopulateStatusDto(
				_state,
				_detailsTotal,
				_detailsDone,
				_imagesTotal,
				_imagesDone,
				_lastMessage);
		}
	}

	public void SetRunning(int detailsTotal, int imagesTotal)
	{
		lock (_gate)
		{
			_state = "running";
			_detailsTotal = Math.Max(0, detailsTotal);
			_detailsDone = 0;
			_imagesTotal = Math.Max(0, imagesTotal);
			_imagesDone = 0;
			_lastMessage = "Populate started.";
		}
	}

	public void SetDetailsDone(int done)
	{
		lock (_gate)
		{
			_detailsDone = Math.Max(0, done);
		}
	}

	public void SetImagesDone(int done)
	{
		lock (_gate)
		{
			_imagesDone = Math.Max(0, done);
		}
	}

	public void SetMessage(string? message)
	{
		lock (_gate)
		{
			_lastMessage = message;
		}
	}

	public void SetIdle()
	{
		lock (_gate)
		{
			_state = "idle";
			_lastMessage = _lastMessage ?? "Idle.";
		}
	}
}
