// Kanban Board JavaScript
let allCards = [];
let allLabels = [];
let allColumns = [];
let allPriorities = [];
let allProjects = [];
let currentProjectId = null;
let pendingDeleteProjectId = null;
let draggedCard = null;

const API = {
    cards: '/api/cards',
    labels: '/api/labels',
    columns: '/api/columns',
    priorities: '/api/priorities',
    projects: '/api/projects'
};

// Automatically scope every project-owned API call to the current project by
// appending ?projectId=. The Projects endpoint itself is intentionally excluded.
const _nativeFetch = window.fetch.bind(window);
window.fetch = function (input, init) {
    if (typeof input === 'string'
        && input.startsWith('/api/')
        && !input.startsWith(API.projects)
        && currentProjectId) {
        const sep = input.includes('?') ? '&' : '?';
        input = `${input}${sep}projectId=${encodeURIComponent(currentProjectId)}`;
    }
    return _nativeFetch(input, init);
};

// ── Init ──
document.addEventListener('DOMContentLoaded', async () => {
    showBoardLoading();
    try {
        await loadProjects();
        await loadColumns();
        await loadLabels();
        await loadPriorities();
        await loadCards();
        setupSearch();
    } catch (err) {
        console.error('Failed to load board data:', err);
        showBoardError();
    }
});

// ── Board loading / error states ──
function showBoardLoading() {
    const board = document.getElementById('kanbanBoard');
    if (!board) return;
    board.setAttribute('aria-busy', 'true');
    const col = () => `
        <div class="skeleton-column" aria-hidden="true">
            <div class="skeleton-line" style="width:60%"></div>
            <div class="skeleton-card"></div>
            <div class="skeleton-card"></div>
            <div class="skeleton-card" style="height:50px"></div>
        </div>`;
    board.innerHTML = `<div class="skeleton-board">${col()}${col()}${col()}</div>`;
}

function showBoardError() {
    const board = document.getElementById('kanbanBoard');
    if (!board) return;
    board.setAttribute('aria-busy', 'false');
    board.innerHTML = `
        <div class="board-state board-error" role="alert">
            <div class="board-state-inner">
                <span class="state-icon" aria-hidden="true">\u26D4</span>
                <h2>Couldn't load the board</h2>
                <p>Something went wrong while loading your data. Check your connection and try again.</p>
                <button type="button" class="btn btn-primary btn-sm" onclick="retryLoad()">Retry</button>
            </div>
        </div>`;
}

async function retryLoad() {
    showBoardLoading();
    try {
        await loadColumns();
        await loadLabels();
        await loadPriorities();
        await loadCards();
    } catch (err) {
        console.error('Retry failed:', err);
        showBoardError();
    }
}

// ── Data Loading ──
async function loadCards() {
    const res = await fetch(API.cards);
    allCards = await res.json();
    renderCards();
}

async function loadLabels() {
    const res = await fetch(API.labels);
    allLabels = await res.json();
    renderLabelFilter();
}

async function loadPriorities() {
    const res = await fetch(API.priorities);
    allPriorities = await res.json();
}

async function loadColumns() {
    const res = await fetch(API.columns);
    allColumns = await res.json();
    renderBoard();
}

// ── Projects ──
async function loadProjects() {
    const res = await fetch(API.projects);
    allProjects = await res.json();

    const saved = localStorage.getItem('currentProjectId');
    if (saved && allProjects.some(p => p.id === saved)) {
        currentProjectId = saved;
    } else if (allProjects.length > 0) {
        currentProjectId = allProjects[0].id;
    } else {
        currentProjectId = null;
    }
    if (currentProjectId) localStorage.setItem('currentProjectId', currentProjectId);
    renderProjectSelect();
}

function renderProjectSelect() {
    const select = document.getElementById('projectSelect');
    if (!select) return;
    select.innerHTML = '';
    allProjects.forEach(p => {
        const opt = document.createElement('option');
        opt.value = p.id;
        opt.textContent = p.name;
        select.appendChild(opt);
    });
    if (currentProjectId) select.value = currentProjectId;
}

async function onProjectChange(id) {
    if (!id || id === currentProjectId) return;
    currentProjectId = id;
    localStorage.setItem('currentProjectId', id);
    await reloadCurrentProject();
}

async function reloadCurrentProject() {
    await loadColumns();
    await loadLabels();
    await loadPriorities();
    await loadCards();
}

// ── Board layout (dynamic columns) ──
function renderBoard() {
    const board = document.getElementById('kanbanBoard');
    if (!board) {
        console.error("renderBoard: '#kanbanBoard' element not found in DOM. The page may not contain the Kanban board markup, or a stale cached page is being served by the service worker.");
        return;
    }
    board.innerHTML = '';
    board.setAttribute('aria-busy', 'false');

    if (!allColumns || allColumns.length === 0) {
        board.innerHTML = `
            <div class="board-state" role="status">
                <div class="board-state-inner">
                    <span class="state-icon" aria-hidden="true">\uD83D\uDDC2\uFE0F</span>
                    <h2>No columns yet</h2>
                    <p>Add your first column to start organizing cards on this board.</p>
                    <button type="button" class="btn btn-primary btn-sm" onclick="openColumnManager()">Add column</button>
                </div>
            </div>`;
        return;
    }

    allColumns
        .slice()
        .sort((a, b) => a.position - b.position)
        .forEach(col => {
            const colEl = document.createElement('div');
            colEl.className = 'kanban-column';
            colEl.dataset.column = col.id;
            const collapsed = isColumnCollapsed(col.id);
            if (collapsed) colEl.classList.add('collapsed');
            const currentSort = getColumnSort(col.id);
            const sortOption = (value, text) =>
                `<option value="${value}"${currentSort === value ? ' selected' : ''}>${text}</option>`;
            const safeTitle = escapeHtml(col.title);
            colEl.innerHTML = `
                <div class="column-header">
                    <button class="btn-collapse-column" title="Collapse/expand column" aria-label="Collapse or expand ${safeTitle} column" aria-expanded="${!collapsed}">${collapsed ? '\u25B6' : '\u25BC'}</button>
                    <span class="column-title">${safeTitle}</span>
                    <span class="card-count" id="count-${col.id}" aria-label="Card count">0</span>
                    <select class="column-sort" title="Sort cards" aria-label="Sort cards in ${safeTitle}">
                        ${sortOption('position', 'Position')}
                        ${sortOption('title', 'Title')}
                        ${sortOption('priority', 'Priority')}
                        ${sortOption('label', 'Label')}
                    </select>
                    <button class="btn-add-card" title="Add card" aria-label="Add card to ${safeTitle}">+</button>
                </div>
                <div class="card-list" id="list-${col.id}"></div>
            `;
            colEl.querySelector('.btn-add-card').addEventListener('click', () => openCardModal(col.id));
            colEl.querySelector('.btn-collapse-column').addEventListener('click', () => toggleColumnCollapse(col.id));
            colEl.querySelector('.column-sort').addEventListener('change', (e) => onColumnSortChange(col.id, e.target.value));
            board.appendChild(colEl);
        });
    setupDragAndDrop();
}

// ── Column collapse/expand ──
function collapsedStorageKey() {
    return `collapsedColumns:${currentProjectId || 'default'}`;
}

function getCollapsedColumns() {
    try {
        const raw = localStorage.getItem(collapsedStorageKey());
        return raw ? JSON.parse(raw) : [];
    } catch {
        return [];
    }
}

function isColumnCollapsed(columnId) {
    return getCollapsedColumns().includes(columnId);
}

function setColumnCollapsed(columnId, collapsed) {
    const set = new Set(getCollapsedColumns());
    if (collapsed) {
        set.add(columnId);
    } else {
        set.delete(columnId);
    }
    localStorage.setItem(collapsedStorageKey(), JSON.stringify([...set]));
}

function toggleColumnCollapse(columnId) {
    const colEl = document.querySelector(`.kanban-column[data-column="${columnId}"]`);
    if (!colEl) return;
    const collapsed = colEl.classList.toggle('collapsed');
    setColumnCollapsed(columnId, collapsed);
    const btn = colEl.querySelector('.btn-collapse-column');
    if (btn) {
        btn.textContent = collapsed ? '\u25B6' : '\u25BC';
        btn.setAttribute('aria-expanded', String(!collapsed));
    }
}

// ── Per-column sorting ──
function columnSortStorageKey() {
    return `columnSort:${currentProjectId || 'default'}`;
}

function getColumnSortMap() {
    try {
        const raw = localStorage.getItem(columnSortStorageKey());
        return raw ? JSON.parse(raw) : {};
    } catch {
        return {};
    }
}

function getColumnSort(columnId) {
    return getColumnSortMap()[columnId] || 'position';
}

function setColumnSort(columnId, sort) {
    const map = getColumnSortMap();
    map[columnId] = sort;
    localStorage.setItem(columnSortStorageKey(), JSON.stringify(map));
}

function onColumnSortChange(columnId, sort) {
    setColumnSort(columnId, sort);
    renderCards();
}

// ── Rendering ──
function renderCards() {
    const columns = allColumns.map(c => c.id);
    const searchTerm = document.getElementById('searchInput').value.toLowerCase();
    const labelFilter = document.getElementById('labelFilter').value;

    columns.forEach(col => {
        const list = document.getElementById(`list-${col}`);
        if (!list) return;
        const priorityOrdinal = (id) => {
            if (!id) return -1;
            const idx = allPriorities.findIndex(p => p.id === id);
            return idx === -1 ? -1 : idx;
        };
        const labelSortKey = (card) => {
            if (!card.labelIds || card.labelIds.length === 0) return '';
            const names = card.labelIds
                .map(lid => allLabels.find(l => l.id === lid))
                .filter(Boolean)
                .map(l => l.name.toLowerCase())
                .sort();
            return names.length ? names[0] : '';
        };
        const sortMode = getColumnSort(col);
        const cards = allCards
            .filter(c => c.column === col)
            .sort((a, b) => {
                switch (sortMode) {
                    case 'title':
                        return a.title.localeCompare(b.title) || a.position - b.position;
                    case 'priority': {
                        const diff = priorityOrdinal(b.priorityId) - priorityOrdinal(a.priorityId);
                        return diff !== 0 ? diff : a.position - b.position;
                    }
                    case 'label':
                        return labelSortKey(a).localeCompare(labelSortKey(b)) || a.position - b.position;
                    case 'position':
                    default:
                        return a.position - b.position;
                }
            });

        list.innerHTML = '';
        let visibleCount = 0;

        cards.forEach(card => {
            const matchesSearch = !searchTerm ||
                card.title.toLowerCase().includes(searchTerm) ||
                card.description.toLowerCase().includes(searchTerm);
            const matchesLabel = !labelFilter ||
                card.labelIds.includes(labelFilter);
            const visible = matchesSearch && matchesLabel;
            if (visible) visibleCount++;

            const el = document.createElement('div');
            el.className = `kanban-card${visible ? '' : ' hidden'}`;
            el.draggable = true;
            el.dataset.id = card.id;
            el.tabIndex = 0;
            el.setAttribute('role', 'button');
            el.setAttribute('aria-label', `Card: ${card.title}. Press Enter to edit.`);

            const priority = card.priorityId ? allPriorities.find(p => p.id === card.priorityId) : null;
            // Clean flat SaaS: white surface with a colored left accent indicating priority.
            el.style.background = 'var(--bg-surface)';
            if (priority) {
                el.style.borderLeft = `3px solid ${priority.color}`;
            }

            const labelsHtml = card.labelIds
                .map(lid => {
                    const label = allLabels.find(l => l.id === lid);
                    return label
                        ? `<span class="label-chip" style="background:${label.color}">${escapeHtml(label.name)}</span>`
                        : '';
                })
                .join('');

            el.innerHTML = `
                ${labelsHtml ? `<div class="card-labels">${labelsHtml}</div>` : ''}
                <div class="card-title">${escapeHtml(card.title)}</div>
                ${card.description ? `<div class="card-description">${renderMarkdown(card.description)}</div>` : ''}
                <div class="card-footer">
                    ${renderPriorityChip(card.priorityId)}
                </div>
                <div class="card-actions-row">
                    ${(card.attachments && card.attachments.length)
                        ? `<div class="card-attachment-indicator" title="${card.attachments.length} attachment(s)" aria-label="${card.attachments.length} attachment(s)">📎 ${card.attachments.length}</div>`
                        : ''}
                    <div class="card-actions">
                        <button onclick="editCard('${card.id}')" title="Edit" aria-label="Edit card ${escapeHtml(card.title)}">✏️ Edit</button>
                        <button onclick="deleteCardDirect('${card.id}')" title="Delete" aria-label="Delete card ${escapeHtml(card.title)}">🗑️</button>
                    </div>
                </div>
            `;

            // Drag events on card
            el.addEventListener('dragstart', handleDragStart);
            el.addEventListener('dragend', handleDragEnd);
            // Keyboard: open the card for editing on Enter/Space (ignore action buttons)
            el.addEventListener('keydown', (e) => {
                if ((e.key === 'Enter' || e.key === ' ') && e.target === el) {
                    e.preventDefault();
                    editCard(card.id);
                }
            });

            list.appendChild(el);
        });

        if (visibleCount === 0) {
            const empty = document.createElement('div');
            empty.className = 'column-empty';
            empty.innerHTML = `
                <span class="empty-icon" aria-hidden="true">🗒️</span>
                <span>${searchTerm || labelFilter ? 'No matching cards' : 'No cards yet'}</span>`;
            list.appendChild(empty);
        }

        const countEl = document.getElementById(`count-${col}`);
        if (countEl) countEl.textContent = visibleCount;
    });
}

function renderLabelFilter() {
    const select = document.getElementById('labelFilter');
    const current = select.value;
    select.innerHTML = '<option value="">All Labels</option>';
    allLabels.forEach(l => {
        const opt = document.createElement('option');
        opt.value = l.id;
        opt.textContent = l.name;
        opt.style.color = l.color;
        select.appendChild(opt);
    });
    select.value = current;
}

// ── Search & Filter ──
function setupSearch() {
    document.getElementById('searchInput').addEventListener('input', renderCards);
    document.getElementById('labelFilter').addEventListener('change', renderCards);
}

// ── Card CRUD ──
function openCardModal(column) {
    document.getElementById('cardModalTitle').textContent = 'Add Card';
    document.getElementById('cardId').value = '';
    document.getElementById('cardColumn').value = column;
    document.getElementById('cardTitle').value = '';
    document.getElementById('cardDescription').value = '';
    document.getElementById('btnDeleteCard').style.display = 'none';
    renderCardLabelCheckboxes([]);
    renderCardPriorityOptions('');
    // Attachments require a saved card; hide the section until the card exists.
    document.getElementById('cardAttachmentsSection').style.display = 'none';
    document.getElementById('cardAttachments').innerHTML = '';
    const input = document.getElementById('cardAttachmentInput');
    if (input) input.value = '';
    new bootstrap.Modal(document.getElementById('cardModal')).show();
}

function editCard(id) {
    const card = allCards.find(c => c.id === id);
    if (!card) return;
    document.getElementById('cardModalTitle').textContent = 'Edit Card';
    document.getElementById('cardId').value = card.id;
    document.getElementById('cardColumn').value = card.column;
    document.getElementById('cardTitle').value = card.title;
    document.getElementById('cardDescription').value = card.description;
    document.getElementById('btnDeleteCard').style.display = 'inline-block';
    renderCardLabelCheckboxes(card.labelIds);
    renderCardPriorityOptions(card.priorityId || '');
    document.getElementById('cardAttachmentsSection').style.display = 'block';
    const input = document.getElementById('cardAttachmentInput');
    if (input) input.value = '';
    renderCardAttachments(card);
    new bootstrap.Modal(document.getElementById('cardModal')).show();
}

function renderCardAttachments(card) {
    const container = document.getElementById('cardAttachments');
    if (!container) return;
    const attachments = card.attachments || [];
    if (attachments.length === 0) {
        container.innerHTML = '<small class="text-muted">No attachments yet.</small>';
        return;
    }
    container.innerHTML = attachments.map(a => `
        <div class="attachment-item" data-id="${a.id}">
            <a href="${API.cards}/${card.id}/attachments/${a.id}${currentProjectId ? `?projectId=${encodeURIComponent(currentProjectId)}` : ''}" target="_blank" rel="noopener" title="Open ${escapeHtml(a.fileName)}">📎 ${escapeHtml(a.fileName)}</a>
            <button type="button" class="btn-remove-attachment" onclick="deleteAttachment('${a.id}')" title="Remove attachment" aria-label="Remove ${escapeHtml(a.fileName)}">🗑️</button>
        </div>
    `).join('');
}

async function uploadAttachments() {
    const cardId = document.getElementById('cardId').value;
    if (!cardId) { showToast('Save the card before adding attachments', 'warning'); return; }
    const input = document.getElementById('cardAttachmentInput');
    if (!input || !input.files || input.files.length === 0) {
        showToast('Choose a file to upload', 'warning');
        return;
    }

    const btn = document.getElementById('btnUploadAttachment');
    if (btn) btn.disabled = true;
    try {
        for (const file of input.files) {
            const formData = new FormData();
            formData.append('file', file);
            const res = await fetch(`${API.cards}/${cardId}/attachments`, {
                method: 'POST',
                body: formData
            });
            if (!res.ok) {
                showToast(`Failed to upload ${file.name}`, 'danger');
            }
        }
    } finally {
        if (btn) btn.disabled = false;
    }

    input.value = '';
    await loadCards();
    const card = allCards.find(c => c.id === cardId);
    if (card) renderCardAttachments(card);
    showToast('Attachment(s) uploaded', 'success');
}

async function deleteAttachment(attachmentId) {
    const cardId = document.getElementById('cardId').value;
    if (!cardId || !confirm('Remove this attachment?')) return;
    const res = await fetch(`${API.cards}/${cardId}/attachments/${attachmentId}`, { method: 'DELETE' });
    if (!res.ok) { showToast('Failed to remove attachment', 'danger'); return; }
    await loadCards();
    const card = allCards.find(c => c.id === cardId);
    if (card) renderCardAttachments(card);
    showToast('Attachment removed', 'success');
}

function renderCardLabelCheckboxes(selectedIds) {
    const container = document.getElementById('cardLabels');
    container.innerHTML = '';
    if (allLabels.length === 0) {
        container.innerHTML = '<small class="text-muted">No labels yet. Create labels first.</small>';
        return;
    }
    allLabels.forEach(label => {
        const isSelected = selectedIds.includes(label.id);
        const div = document.createElement('div');
        div.className = `label-checkbox${isSelected ? ' selected' : ''}`;
        div.style.backgroundColor = label.color;
        div.innerHTML = `
            <input type="checkbox" value="${label.id}" ${isSelected ? 'checked' : ''} />
            ${escapeHtml(label.name)}
        `;
        div.addEventListener('click', () => {
            const cb = div.querySelector('input');
            cb.checked = !cb.checked;
            div.classList.toggle('selected', cb.checked);
        });
        container.appendChild(div);
    });
}

async function saveCard() {
    const id = document.getElementById('cardId').value;
    const title = document.getElementById('cardTitle').value.trim();
    if (!title) { showToast('Title is required', 'warning'); return; }

    const labelIds = Array.from(document.querySelectorAll('#cardLabels input:checked'))
        .map(cb => cb.value);

    const priorityId = document.getElementById('cardPriority').value || null;

    const body = {
        title,
        description: document.getElementById('cardDescription').value.trim(),
        column: document.getElementById('cardColumn').value,
        labelIds,
        priorityId
    };

    if (id) {
        await fetch(`${API.cards}/${id}`, {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(body)
        });
    } else {
        await fetch(API.cards, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(body)
        });
    }

    bootstrap.Modal.getInstance(document.getElementById('cardModal')).hide();
    await loadCards();
    showToast(id ? 'Card updated' : 'Card created', 'success');
}

async function deleteCard() {
    const id = document.getElementById('cardId').value;
    if (!id || !confirm('Delete this card?')) return;
    await fetch(`${API.cards}/${id}`, { method: 'DELETE' });
    bootstrap.Modal.getInstance(document.getElementById('cardModal')).hide();
    await loadCards();
    showToast('Card deleted', 'success');
}

async function deleteCardDirect(id) {
    if (!confirm('Delete this card?')) return;
    await fetch(`${API.cards}/${id}`, { method: 'DELETE' });
    await loadCards();
    showToast('Card deleted', 'success');
}

// ── Drag & Drop ──
function setupDragAndDrop() {
    // Bind to the whole column so the entire column is a drop target,
    // not just the narrow card-list bounds.
    document.querySelectorAll('.kanban-column').forEach(col => {
        col.addEventListener('dragover', handleDragOver);
        col.addEventListener('dragenter', handleDragEnter);
        col.addEventListener('dragleave', handleDragLeave);
        col.addEventListener('drop', handleDrop);
    });
}

function handleDragStart(e) {
    draggedCard = e.target.closest('.kanban-card');
    draggedCard.classList.add('dragging');
    e.dataTransfer.effectAllowed = 'move';
    e.dataTransfer.setData('text/plain', draggedCard.dataset.id);
}

function handleDragEnd() {
    if (draggedCard) {
        draggedCard.classList.remove('dragging');
        draggedCard = null;
    }
    document.querySelectorAll('.drag-over').forEach(el => el.classList.remove('drag-over'));
    document.querySelectorAll('.drop-placeholder').forEach(el => el.remove());
}

function handleDragEnter(e) {
    e.preventDefault();
    const col = e.target.closest('.kanban-column');
    if (col) col.classList.add('drag-over');
}

function handleDragLeave(e) {
    const col = e.target.closest('.kanban-column');
    if (col && !col.contains(e.relatedTarget)) {
        col.classList.remove('drag-over');
    }
}

function handleDragOver(e) {
    e.preventDefault();
    e.dataTransfer.dropEffect = 'move';

    const col = e.target.closest('.kanban-column');
    if (!col) return;
    const list = col.querySelector('.card-list');
    if (!list) return;

    col.classList.add('drag-over');

    // Remove existing placeholders (across all columns)
    document.querySelectorAll('.drop-placeholder').forEach(el => el.remove());

    const afterElement = getDragAfterElement(list, e.clientY);
    const placeholder = document.createElement('div');
    placeholder.className = 'drop-placeholder';

    if (afterElement) {
        list.insertBefore(placeholder, afterElement);
    } else {
        list.appendChild(placeholder);
    }
}

async function handleDrop(e) {
    e.preventDefault();
    const col = e.target.closest('.kanban-column');
    if (!col) return;
    const list = col.querySelector('.card-list');
    if (!list) return;

    col.classList.remove('drag-over');
    document.querySelectorAll('.drop-placeholder').forEach(el => el.remove());

    const cardId = e.dataTransfer.getData('text/plain');
    const column = col.dataset.column;

    // Calculate position
    const visibleCards = Array.from(list.querySelectorAll('.kanban-card:not(.dragging):not(.hidden)'));
    const afterElement = getDragAfterElement(list, e.clientY);
    let position;
    if (afterElement) {
        position = visibleCards.indexOf(afterElement);
        if (position < 0) position = visibleCards.length;
    } else {
        position = visibleCards.length;
    }

    await fetch(`${API.cards}/${cardId}/move`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ column, position })
    });

    await loadCards();
}

function getDragAfterElement(container, y) {
    const draggableElements = Array.from(
        container.querySelectorAll('.kanban-card:not(.dragging):not(.hidden)')
    );

    return draggableElements.reduce((closest, child) => {
        const box = child.getBoundingClientRect();
        const offset = y - box.top - box.height / 2;
        if (offset < 0 && offset > closest.offset) {
            return { offset, element: child };
        }
        return closest;
    }, { offset: Number.NEGATIVE_INFINITY }).element;
}

// ── Label Management ──
function openLabelManager() {
    renderLabelList();
    new bootstrap.Modal(document.getElementById('labelModal')).show();
}

function renderLabelList() {
    const container = document.getElementById('labelList');
    if (allLabels.length === 0) {
        container.innerHTML = '<p class="text-muted">No labels yet.</p>';
        return;
    }
    container.innerHTML = allLabels.map(l => `
        <div class="label-item" data-id="${l.id}">
            <div class="label-preview" style="background:${l.color}"></div>
            <input type="text" value="${escapeHtml(l.name)}" data-field="name" />
            <input type="color" value="${l.color}" data-field="color" style="width:36px;height:28px;padding:0;border:none;" />
            <button onclick="updateLabel('${l.id}')" title="Save">💾</button>
            <button onclick="deleteLabel('${l.id}')" title="Delete">🗑️</button>
        </div>
    `).join('');
}

async function createLabel() {
    const name = document.getElementById('newLabelName').value.trim();
    const color = document.getElementById('newLabelColor').value;
    if (!name) { showToast('Label name is required', 'warning'); return; }

    await fetch(API.labels, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ name, color })
    });

    document.getElementById('newLabelName').value = '';
    await loadLabels();
    renderLabelList();
    showToast('Label created', 'success');
}

async function updateLabel(id) {
    const item = document.querySelector(`.label-item[data-id="${id}"]`);
    const name = item.querySelector('[data-field="name"]').value.trim();
    const color = item.querySelector('[data-field="color"]').value;
    if (!name) { showToast('Label name is required', 'warning'); return; }

    await fetch(`${API.labels}/${id}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ name, color })
    });

    await loadLabels();
    renderLabelList();
    renderCards();
    showToast('Label updated', 'success');
}

async function deleteLabel(id) {
    if (!confirm('Delete this label? It will be removed from all cards.')) return;
    await fetch(`${API.labels}/${id}`, { method: 'DELETE' });
    await loadLabels();
    renderLabelList();
    await loadCards();
    showToast('Label deleted', 'success');
}

// ── Priority Management ──
function renderCardPriorityOptions(selectedId) {
    const select = document.getElementById('cardPriority');
    select.innerHTML = '<option value="">None</option>';
    allPriorities.forEach(p => {
        const opt = document.createElement('option');
        opt.value = p.id;
        opt.textContent = p.name;
        opt.style.color = p.color;
        if (p.id === selectedId) opt.selected = true;
        select.appendChild(opt);
    });
}

function renderPriorityChip(priorityId) {
    if (!priorityId) return '';
    const p = allPriorities.find(x => x.id === priorityId);
    if (!p) return '';
    return `<span class="priority-chip" style="background:${p.color}">${escapeHtml(p.name)}</span>`;
}

function openPriorityManager() {
    renderPriorityList();
    new bootstrap.Modal(document.getElementById('priorityModal')).show();
}

function renderPriorityList() {
    const container = document.getElementById('priorityList');
    if (allPriorities.length === 0) {
        container.innerHTML = '<p class="text-muted">No priorities yet.</p>';
        return;
    }
    container.innerHTML = allPriorities.map(p => `
        <div class="label-item" data-id="${p.id}">
            <div class="label-preview" style="background:${p.color}"></div>
            <input type="text" value="${escapeHtml(p.name)}" data-field="name" />
            <input type="color" value="${p.color}" data-field="color" style="width:36px;height:28px;padding:0;border:none;" />
            <button onclick="updatePriority('${p.id}')" title="Save">💾</button>
            <button onclick="deletePriority('${p.id}')" title="Delete">🗑️</button>
        </div>
    `).join('');
}

async function createPriority() {
    const name = document.getElementById('newPriorityName').value.trim();
    const color = document.getElementById('newPriorityColor').value;
    if (!name) { showToast('Priority name is required', 'warning'); return; }

    await fetch(API.priorities, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ name, color })
    });

    document.getElementById('newPriorityName').value = '';
    await loadPriorities();
    renderPriorityList();
    showToast('Priority created', 'success');
}

async function updatePriority(id) {
    const item = document.querySelector(`#priorityList .label-item[data-id="${id}"]`);
    const name = item.querySelector('[data-field="name"]').value.trim();
    const color = item.querySelector('[data-field="color"]').value;
    if (!name) { showToast('Priority name is required', 'warning'); return; }

    await fetch(`${API.priorities}/${id}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ name, color })
    });

    await loadPriorities();
    renderPriorityList();
    renderCards();
    showToast('Priority updated', 'success');
}

async function deletePriority(id) {
    if (!confirm('Delete this priority? It will be removed from all cards.')) return;
    await fetch(`${API.priorities}/${id}`, { method: 'DELETE' });
    await loadPriorities();
    renderPriorityList();
    await loadCards();
    showToast('Priority deleted', 'success');
}

// ── Column Management ──
function openColumnManager() {
    renderColumnList();
    new bootstrap.Modal(document.getElementById('columnModal')).show();
}

function renderColumnList() {
    const container = document.getElementById('columnList');
    if (allColumns.length === 0) {
        container.innerHTML = '<p class="text-muted">No columns yet.</p>';
        return;
    }
    const ordered = allColumns.slice().sort((a, b) => a.position - b.position);
    container.innerHTML = ordered.map(c => {
        const count = allCards.filter(card => card.column === c.id).length;
        return `
        <div class="column-item" draggable="true" data-id="${c.id}">
            <span class="drag-handle" title="Drag to reorder">⠿</span>
            <input type="text" value="${escapeHtml(c.title)}" data-field="title" />
            <span class="text-muted small">${count} card${count === 1 ? '' : 's'}</span>
            <button onclick="updateColumn('${c.id}')" title="Save">💾</button>
            <button onclick="deleteColumn('${c.id}')" title="Delete">🗑️</button>
        </div>`;
    }).join('');

    container.querySelectorAll('.column-item').forEach(item => {
        item.addEventListener('dragstart', handleColumnDragStart);
        item.addEventListener('dragover', handleColumnDragOver);
        item.addEventListener('drop', handleColumnDrop);
        item.addEventListener('dragend', handleColumnDragEnd);
    });
}

let draggedColumnItem = null;
function handleColumnDragStart(e) {
    draggedColumnItem = e.currentTarget;
    e.dataTransfer.effectAllowed = 'move';
    draggedColumnItem.classList.add('dragging');
}
function handleColumnDragOver(e) {
    e.preventDefault();
    const target = e.currentTarget;
    if (!draggedColumnItem || target === draggedColumnItem) return;
    const rect = target.getBoundingClientRect();
    const after = (e.clientY - rect.top) > rect.height / 2;
    target.parentNode.insertBefore(draggedColumnItem, after ? target.nextSibling : target);
}
function handleColumnDrop(e) { e.preventDefault(); }
async function handleColumnDragEnd() {
    if (draggedColumnItem) draggedColumnItem.classList.remove('dragging');
    draggedColumnItem = null;
    const orderedIds = Array.from(document.querySelectorAll('#columnList .column-item'))
        .map(el => el.dataset.id);
    await fetch(`${API.columns}/reorder`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(orderedIds)
    });
    await loadColumns();
    await loadCards();
    renderColumnList();
}

async function createColumn() {
    const titleEl = document.getElementById('newColumnTitle');
    const title = titleEl.value.trim();
    if (!title) { showToast('Column title is required', 'warning'); return; }
    const res = await fetch(API.columns, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ title })
    });
    if (!res.ok) { showToast('Failed to create column', 'error'); return; }
    titleEl.value = '';
    await loadColumns();
    renderColumnList();
    await loadCards();
    showToast('Column created', 'success');
}

async function updateColumn(id) {
    const item = document.querySelector(`.column-item[data-id="${id}"]`);
    const title = item.querySelector('[data-field="title"]').value.trim();
    if (!title) { showToast('Column title is required', 'warning'); return; }
    const res = await fetch(`${API.columns}/${id}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ title })
    });
    if (!res.ok) { showToast('Failed to update column', 'error'); return; }
    await loadColumns();
    renderColumnList();
    await loadCards();
    showToast('Column updated', 'success');
}

async function deleteColumn(id) {
    if (!confirm('Delete this column?')) return;
    let res = await fetch(`${API.columns}/${id}`, { method: 'DELETE' });
    if (res.status === 409) {
        const body = await res.json().catch(() => ({}));
        const n = body.cardCount ?? '';
        if (!confirm(`This column has ${n} card(s). Delete the column AND its cards?`)) return;
        res = await fetch(`${API.columns}/${id}?force=true`, { method: 'DELETE' });
    }
    if (!res.ok && res.status !== 204) { showToast('Failed to delete column', 'error'); return; }
    await loadColumns();
    renderColumnList();
    await loadCards();
    showToast('Column deleted', 'success');
}

// ── Project Management ──
function openProjectManager() {
    renderProjectList();
    new bootstrap.Modal(document.getElementById('projectModal')).show();
}

function renderProjectList() {
    const container = document.getElementById('projectList');
    if (allProjects.length === 0) {
        container.innerHTML = '<p class="text-muted">No projects yet.</p>';
        return;
    }
    container.innerHTML = allProjects.map(p => {
        const isCurrent = p.id === currentProjectId;
        return `
        <div class="label-item" data-id="${p.id}">
            <input type="text" value="${escapeHtml(p.name)}" data-field="name" />
            ${isCurrent ? '<span class="text-muted small">current</span>' : ''}
            <button onclick="updateProject('${p.id}')" title="Save">💾</button>
            <button onclick="deleteProject('${p.id}')" title="Delete">🗑️</button>
        </div>`;
    }).join('');
}

async function createProject() {
    const input = document.getElementById('newProjectName');
    const name = input.value.trim();
    if (!name) { showToast('Project name is required', 'warning'); return; }

    const res = await fetch(API.projects, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ name })
    });
    if (!res.ok) { showToast('Failed to create project', 'error'); return; }

    const created = await res.json();
    input.value = '';
    await loadProjects();
    renderProjectList();
    showToast('Project created', 'success');

    // Switch to the newly created project.
    if (created && created.id) {
        currentProjectId = created.id;
        localStorage.setItem('currentProjectId', currentProjectId);
        renderProjectSelect();
        await reloadCurrentProject();
    }
}

async function updateProject(id) {
    const item = document.querySelector(`#projectList .label-item[data-id="${id}"]`);
    const name = item.querySelector('[data-field="name"]').value.trim();
    if (!name) { showToast('Project name is required', 'warning'); return; }

    const res = await fetch(`${API.projects}/${id}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ name })
    });
    if (!res.ok) { showToast('Failed to update project', 'error'); return; }

    await loadProjects();
    renderProjectList();
    showToast('Project updated', 'success');
}

function deleteProject(id) {
    if (allProjects.length <= 1) {
        showToast('You cannot delete the last remaining project.', 'warning');
        return;
    }
    const project = allProjects.find(p => p.id === id);
    pendingDeleteProjectId = id;
    document.getElementById('deleteProjectName').textContent = project ? project.name : '';
    new bootstrap.Modal(document.getElementById('projectDeleteModal')).show();
}

async function confirmDeleteProject() {
    const id = pendingDeleteProjectId;
    if (!id) return;

    const res = await fetch(`${API.projects}/${id}`, { method: 'DELETE' });
    if (!res.ok && res.status !== 204) {
        const body = await res.json().catch(() => ({}));
        showToast(body.error || 'Failed to delete project', 'error');
        return;
    }

    pendingDeleteProjectId = null;
    bootstrap.Modal.getInstance(document.getElementById('projectDeleteModal'))?.hide();

    const wasCurrent = id === currentProjectId;
    await loadProjects();

    // If the active project was removed, switch to whatever remains.
    if (wasCurrent) {
        currentProjectId = allProjects.length > 0 ? allProjects[0].id : null;
        if (currentProjectId) localStorage.setItem('currentProjectId', currentProjectId);
        renderProjectSelect();
    }
    await reloadCurrentProject();
    renderProjectList();
    showToast('Project deleted', 'success');
}

// ── Util ──
function hexToRgba(hex, alpha) {
    if (!hex) return `rgba(0,121,191,${alpha})`;
    let h = hex.replace('#', '');
    if (h.length === 3) h = h.split('').map(c => c + c).join('');
    const r = parseInt(h.substring(0, 2), 16);
    const g = parseInt(h.substring(2, 4), 16);
    const b = parseInt(h.substring(4, 6), 16);
    return `rgba(${r},${g},${b},${alpha})`;
}

function renderMarkdown(text) {
    if (typeof marked !== 'undefined') {
        marked.setOptions({ breaks: true, gfm: true });
        return marked.parse(text);
    }
    return escapeHtml(text);
}

function escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
}

// ── Theme (dark mode) ──
function applyTheme(theme) {
    document.documentElement.setAttribute('data-theme', theme);
    document.documentElement.setAttribute('data-bs-theme', theme);
    try { localStorage.setItem('theme', theme); } catch { /* ignore */ }
    const toggle = document.getElementById('themeToggle');
    if (toggle) {
        const isDark = theme === 'dark';
        toggle.setAttribute('aria-pressed', String(isDark));
        toggle.setAttribute('aria-label', isDark ? 'Switch to light mode' : 'Switch to dark mode');
        toggle.title = isDark ? 'Switch to light mode' : 'Switch to dark mode';
    }
}

function toggleTheme() {
    const current = document.documentElement.getAttribute('data-theme') === 'dark' ? 'dark' : 'light';
    applyTheme(current === 'dark' ? 'light' : 'dark');
}

// Sync the toggle's accessible state with the theme applied before paint.
document.addEventListener('DOMContentLoaded', () => {
    const current = document.documentElement.getAttribute('data-theme') === 'dark' ? 'dark' : 'light';
    applyTheme(current);
});

// ── Toast notifications ──
function showToast(message, type = 'info', duration = 3200) {
    const container = document.getElementById('toastContainer');
    if (!container) return;

    const icons = { success: '\u2705', error: '\u26D4', warning: '\u26A0\uFE0F', info: '\u2139\uFE0F' };
    const toast = document.createElement('div');
    toast.className = `toast-item toast-${type}`;
    toast.setAttribute('role', type === 'error' ? 'alert' : 'status');
    toast.innerHTML = `
        <span class="toast-icon" aria-hidden="true">${icons[type] || icons.info}</span>
        <span class="toast-message">${escapeHtml(message)}</span>`;
    container.appendChild(toast);

    // Force reflow so the transition runs, then reveal.
    requestAnimationFrame(() => toast.classList.add('show'));

    const remove = () => {
        toast.classList.remove('show');
        setTimeout(() => toast.remove(), 200);
    };
    setTimeout(remove, duration);
}
