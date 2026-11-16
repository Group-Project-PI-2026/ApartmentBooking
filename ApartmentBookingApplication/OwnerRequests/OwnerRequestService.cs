using ApartmentBookingApplication.Abstractions;
using ApartmentBookingApplication.Common;
using ApartmentBookingApplication.Exceptions;
using ApartmentBookingDomain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ApartmentBookingApplication.OwnerRequests
{
    public class OwnerRequestService
    {
        private const int MaxPageSize = 100;

        private readonly IAppDbContext _db;
        private readonly ICurrentUser _currentUser;
        private readonly IEmailSender _emailSender;

        public OwnerRequestService(IAppDbContext db, ICurrentUser currentUser, IEmailSender emailSender)
        {
            _db = db;
            _currentUser = currentUser;
            _emailSender = emailSender;
        }

        // ---------- Owner ----------

        public async Task<OwnerRequestDetailsDto> CreateAsync(CreateOwnerRequestRequest request, CancellationToken ct)
        {
            if (!await _db.Districts.AnyAsync(d => d.Id == request.DistrictId, ct))
                throw new ValidationException($"District {request.DistrictId} does not exist.");

            var ownerRequest = new OwnerRequest(
                _currentUser.UserId,
                request.ContactName,
                request.ContactEmail,
                request.ContactPhone,
                request.DistrictId,
                request.Address,
                request.Title,
                request.Description,
                request.MaxGuests,
                request.ExpectedPricePerNight,
                request.PhotoUrls,
                request.Amenities);

            _db.OwnerRequests.Add(ownerRequest);
            await _db.SaveChangesAsync(ct);

            return await GetDetailsAsync(ownerRequest.Id, ct);
        }

        public async Task<IReadOnlyList<OwnerRequestListItemDto>> GetMyAsync(CancellationToken ct)
        {
            var ownerId = _currentUser.UserId;
            return await ListQuery(_db.OwnerRequests.Where(r => r.OwnerId == ownerId))
                .ToListAsync(ct);
        }

        public async Task<OwnerRequestDetailsDto> GetMyByIdAsync(Guid id, CancellationToken ct)
        {
            var details = await GetDetailsAsync(id, ct);
            if (details.OwnerId != _currentUser.UserId)
                throw new NotFoundException("Owner request", id);
            return details;
        }

        // ---------- Manager ----------

        public async Task<PagedResult<OwnerRequestListItemDto>> GetPagedAsync(OwnerRequestListQuery query, CancellationToken ct)
        {
            var page = Math.Max(1, query.Page);
            var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);

            var requests = _db.OwnerRequests.AsQueryable();

            if (query.Status.HasValue)
                requests = requests.Where(r => r.Status == query.Status.Value);

            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                var term = query.Search.Trim().ToLower();
                var hasNumber = int.TryParse(term, out var number);

                requests = requests.Where(r =>
                    (hasNumber && r.Number == number) ||
                    r.ContactName.ToLower().Contains(term) ||
                    r.Address.ToLower().Contains(term) ||
                    r.ContactPhone.Contains(term) ||
                    r.ContactEmail.ToLower().Contains(term));
            }

            var total = await requests.CountAsync(ct);
            var items = await ListQuery(requests)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(ct);

            return new PagedResult<OwnerRequestListItemDto>(items, page, pageSize, total);
        }

        public Task<OwnerRequestDetailsDto> GetByIdAsync(Guid id, CancellationToken ct) => GetDetailsAsync(id, ct);

        public async Task<OwnerRequestDetailsDto> ApproveAsync(Guid id, ApproveOwnerRequestRequest request, CancellationToken ct)
        {
            var ownerRequest = await LoadAsync(id, ct);

            var apartment = ownerRequest.Approve(
                _currentUser.UserId, request.PricePerNight, request.CheckInInstructions, request.Comment);

            _db.Apartments.Add(apartment);
            await _db.SaveChangesAsync(ct);

            await _emailSender.SendAsync(
                ownerRequest.ContactEmail,
                $"Заявку №{ownerRequest.Number} схвалено",
                $"Вітаємо! Ваше помешкання «{ownerRequest.Title}» додано до каталогу." +
                (string.IsNullOrWhiteSpace(request.Comment) ? string.Empty : $"\n\nКоментар менеджера: {request.Comment}"),
                CancellationToken.None);

            return await GetDetailsAsync(id, ct);
        }

        public async Task<OwnerRequestDetailsDto> RejectAsync(Guid id, string reason, CancellationToken ct)
        {
            var ownerRequest = await LoadAsync(id, ct);
            ownerRequest.Reject(_currentUser.UserId, reason);
            await _db.SaveChangesAsync(ct);

            await _emailSender.SendAsync(
                ownerRequest.ContactEmail,
                $"Заявку №{ownerRequest.Number} відхилено",
                $"На жаль, вашу заявку відхилено.\n\nПричина: {reason}",
                CancellationToken.None);

            return await GetDetailsAsync(id, ct);
        }

        public async Task<OwnerRequestDetailsDto> RequestClarificationAsync(Guid id, string message, CancellationToken ct)
        {
            var ownerRequest = await LoadAsync(id, ct);
            ownerRequest.RequestClarification(_currentUser.UserId, message);
            await _db.SaveChangesAsync(ct);

            await _emailSender.SendAsync(
                ownerRequest.ContactEmail,
                $"Заявка №{ownerRequest.Number}: потрібне уточнення",
                message,
                CancellationToken.None);

            return await GetDetailsAsync(id, ct);
        }

        // ---------- Helpers ----------

        private async Task<OwnerRequest> LoadAsync(Guid id, CancellationToken ct)
        {
            return await _db.OwnerRequests
                       .Include(r => r.History)
                       .FirstOrDefaultAsync(r => r.Id == id, ct)
                   ?? throw new NotFoundException("Owner request", id);
        }

        private IQueryable<OwnerRequestListItemDto> ListQuery(IQueryable<OwnerRequest> requests)
        {
            return from r in requests.AsNoTracking()
                   join d in _db.Districts on r.DistrictId equals d.Id
                   orderby r.Number descending
                   select new OwnerRequestListItemDto(
                       r.Id, r.Number, r.ContactName, r.ContactPhone, r.Address, d.Name, r.Status, r.CreatedAt);
        }

        private async Task<OwnerRequestDetailsDto> GetDetailsAsync(Guid id, CancellationToken ct)
        {
            var r = await _db.OwnerRequests
                        .AsNoTracking()
                        .Include(x => x.History)
                        .FirstOrDefaultAsync(x => x.Id == id, ct)
                    ?? throw new NotFoundException("Owner request", id);

            var districtName = await _db.Districts
                .Where(d => d.Id == r.DistrictId)
                .Select(d => d.Name)
                .FirstOrDefaultAsync(ct) ?? string.Empty;

            return new OwnerRequestDetailsDto(
                r.Id, r.Number, r.OwnerId,
                r.ContactName, r.ContactEmail, r.ContactPhone,
                r.DistrictId, districtName, r.Address,
                r.Title, r.Description, r.MaxGuests, r.ExpectedPricePerNight,
                r.PhotoUrls, r.Amenities,
                r.Status, r.ApartmentId, r.CreatedAt, r.UpdatedAt,
                r.History
                    .OrderBy(h => h.ChangedAt)
                    .Select(h => new OwnerRequestHistoryDto(h.Status, h.Message, h.ChangedBy, h.ChangedAt))
                    .ToList());
        }
    }
}
