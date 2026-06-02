// Kanban Board JavaScript
let allCards = [];
let allLabels = [];
let allColumns = [];
let allPriorities = [];
let draggedCard = null;

const API = {
    cards: '/api/cards',
    labels: '/api/labels',
    columns: '/api/columns',
    priorities: '/api/priorities'
};

// ── Init ──
document.addEventListener('DOMContentLoaded', async () => {
    await loadColumns();
    await loadLabels();
    await loadPriorities();
    await loadCards();
    setupSearch();
});

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

// ── Board layout (dynamic columns) ──
function renderBoard() {
    const board = document.getElementById('kanbanBoard');
    if (!board) {
        console.error("renderBoard: '#kanbanBoard' element not found in DOM. The page may not contain the Kanban board markup, or a stale cached page is being served by the service worker.");
        return;
    }
    board.innerHTML = '';
    allColumns
        .slice()
        .sort((a, b) => a.position - b.position)
        .forEach(col => {
            const colEl = document.createElement('div');
            colEl.className = 'kanban-column';
            colEl.dataset.column = col.id;
            colEl.innerHTML = `
                <div class="column-header">
                    <span class="column-title">${escapeHtml(col.title)}</span>
                    <span class="card-count" id="count-${col.id}">0</span>
                    <button class="btn-add-card" title="Add card">+</button>
                </div>
                <div class="card-list" id="list-${col.id}"></div>
            `;
            colEl.querySelector('.btn-add-card').addEventListener('click', () => openCardModal(col.id));
            board.appendChild(colEl);
        });
    setupDragAndDrop();
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
        const cards = allCards
            .filter(c => c.column === col)
            .sort((a, b) => {
                const diff = priorityOrdinal(b.priorityId) - priorityOrdinal(a.priorityId);
                return diff !== 0 ? diff : a.position - b.position;
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
                <div class="card-actions">
                    <button onclick="editCard('${card.id}')" title="Edit">✏️ Edit</button>
                    <button onclick="deleteCardDirect('${card.id}')" title="Delete">🗑️</button>
                </div>
            `;

            // Drag events on card
            el.addEventListener('dragstart', handleDragStart);
            el.addEventListener('dragend', handleDragEnd);

            list.appendChild(el);
        });

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
    new bootstrap.Modal(document.getElementById('cardModal')).show();
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
    if (!title) { alert('Title is required'); return; }

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
}

async function deleteCard() {
    const id = document.getElementById('cardId').value;
    if (!id || !confirm('Delete this card?')) return;
    await fetch(`${API.cards}/${id}`, { method: 'DELETE' });
    bootstrap.Modal.getInstance(document.getElementById('cardModal')).hide();
    await loadCards();
}

async function deleteCardDirect(id) {
    if (!confirm('Delete this card?')) return;
    await fetch(`${API.cards}/${id}`, { method: 'DELETE' });
    await loadCards();
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
    if (!name) { alert('Label name is required'); return; }

    await fetch(API.labels, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ name, color })
    });

    document.getElementById('newLabelName').value = '';
    await loadLabels();
    renderLabelList();
}

async function updateLabel(id) {
    const item = document.querySelector(`.label-item[data-id="${id}"]`);
    const name = item.querySelector('[data-field="name"]').value.trim();
    const color = item.querySelector('[data-field="color"]').value;
    if (!name) { alert('Label name is required'); return; }

    await fetch(`${API.labels}/${id}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ name, color })
    });

    await loadLabels();
    renderLabelList();
    renderCards();
}

async function deleteLabel(id) {
    if (!confirm('Delete this label? It will be removed from all cards.')) return;
    await fetch(`${API.labels}/${id}`, { method: 'DELETE' });
    await loadLabels();
    renderLabelList();
    await loadCards();
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
    if (!name) { alert('Priority name is required'); return; }

    await fetch(API.priorities, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ name, color })
    });

    document.getElementById('newPriorityName').value = '';
    await loadPriorities();
    renderPriorityList();
}

async function updatePriority(id) {
    const item = document.querySelector(`#priorityList .label-item[data-id="${id}"]`);
    const name = item.querySelector('[data-field="name"]').value.trim();
    const color = item.querySelector('[data-field="color"]').value;
    if (!name) { alert('Priority name is required'); return; }

    await fetch(`${API.priorities}/${id}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ name, color })
    });

    await loadPriorities();
    renderPriorityList();
    renderCards();
}

async function deletePriority(id) {
    if (!confirm('Delete this priority? It will be removed from all cards.')) return;
    await fetch(`${API.priorities}/${id}`, { method: 'DELETE' });
    await loadPriorities();
    renderPriorityList();
    await loadCards();
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
    if (!title) { alert('Column title is required'); return; }
    const res = await fetch(API.columns, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ title })
    });
    if (!res.ok) { alert('Failed to create column'); return; }
    titleEl.value = '';
    await loadColumns();
    renderColumnList();
    await loadCards();
}

async function updateColumn(id) {
    const item = document.querySelector(`.column-item[data-id="${id}"]`);
    const title = item.querySelector('[data-field="title"]').value.trim();
    if (!title) { alert('Column title is required'); return; }
    const res = await fetch(`${API.columns}/${id}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ title })
    });
    if (!res.ok) { alert('Failed to update column'); return; }
    await loadColumns();
    renderColumnList();
    await loadCards();
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
    if (!res.ok && res.status !== 204) { alert('Failed to delete column'); return; }
    await loadColumns();
    renderColumnList();
    await loadCards();
}

// ── Util ──
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
