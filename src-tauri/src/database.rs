use rusqlite::{params, Connection, Result};
use serde::{Deserialize, Serialize};

#[derive(Debug, Serialize, Deserialize, Clone, Copy, PartialEq, Eq)]
pub enum ClipboardItemType {
    Text = 0,
    Image = 1,
    FileList = 2,
    RichText = 3,
}

impl ClipboardItemType {
    pub fn from_i32(val: i32) -> Self {
        match val {
            1 => ClipboardItemType::Image,
            2 => ClipboardItemType::FileList,
            3 => ClipboardItemType::RichText,
            _ => ClipboardItemType::Text,
        }
    }

    pub fn to_i32(&self) -> i32 {
        *self as i32
    }
}

#[derive(Debug, Serialize, Deserialize, Clone)]
pub struct FileClipboardEntry {
    pub path: String,
    pub name: String,
    #[serde(rename = "isDirectory")]
    pub is_directory: bool,
    pub extension: String,
}

#[derive(Debug, Serialize, Deserialize, Clone)]
pub struct ClipboardItem {
    pub id: String,
    #[serde(rename = "type")]
    pub item_type: ClipboardItemType,
    pub summary: String,
    #[serde(rename = "searchText")]
    pub search_text: String,
    #[serde(rename = "textContent")]
    pub text_content: Option<String>,
    #[serde(rename = "htmlContent")]
    pub html_content: Option<String>,
    #[serde(rename = "rtfContent")]
    pub rtf_content: Option<String>,
    #[serde(rename = "imagePath")]
    pub image_path: Option<String>,
    pub files: Option<Vec<FileClipboardEntry>>,
    #[serde(rename = "createdAt")]
    pub created_at: String,
    #[serde(rename = "expiresAt")]
    pub expires_at: Option<String>,
    #[serde(rename = "contentHash")]
    pub content_hash: Option<String>,
    #[serde(rename = "displayMetadata")]
    pub display_metadata: Option<String>,
}

pub fn initialize(db_path: &str) -> Result<()> {
    let conn = Connection::open(db_path)?;
    conn.execute(
        "CREATE TABLE IF NOT EXISTS clipboard_items (
            id TEXT PRIMARY KEY,
            type INTEGER NOT NULL,
            summary TEXT NOT NULL,
            search_text TEXT NOT NULL,
            text_content TEXT,
            html_content TEXT,
            rtf_content TEXT,
            image_path TEXT,
            file_list_json TEXT,
            created_at TEXT NOT NULL,
            expires_at TEXT,
            content_hash TEXT,
            display_metadata TEXT
        );",
        [],
    )?;

    conn.execute(
        "CREATE INDEX IF NOT EXISTS idx_clipboard_items_created_at ON clipboard_items(created_at);",
        [],
    )?;

    conn.execute(
        "CREATE INDEX IF NOT EXISTS idx_clipboard_items_content_hash ON clipboard_items(content_hash);",
        [],
    )?;

    Ok(())
}

pub fn search_items(db_path: &str, query: &str, limit: i32) -> Result<Vec<ClipboardItem>> {
    let conn = Connection::open(db_path)?;
    let mut stmt = conn.prepare(
        "SELECT id, type, summary, search_text, text_content, html_content, rtf_content, image_path, file_list_json, created_at, expires_at, content_hash, display_metadata
         FROM clipboard_items
         WHERE (?1 = '' OR summary LIKE ?2 OR search_text LIKE ?2)
         ORDER BY datetime(created_at) DESC
         LIMIT ?3;"
    )?;

    let pattern = format!("%{}%", query);
    let rows = stmt.query_map(params![query, pattern, limit], |row| {
        let files_json: Option<String> = row.get(8)?;
        let files: Option<Vec<FileClipboardEntry>> = files_json
            .and_then(|json| serde_json::from_str(&json).ok());

        Ok(ClipboardItem {
            id: row.get(0)?,
            item_type: ClipboardItemType::from_i32(row.get(1)?),
            summary: row.get(2)?,
            search_text: row.get(3)?,
            text_content: row.get(4)?,
            html_content: row.get(5)?,
            rtf_content: row.get(6)?,
            image_path: row.get(7)?,
            files,
            created_at: row.get(9)?,
            expires_at: row.get(10)?,
            content_hash: row.get(11)?,
            display_metadata: row.get(12)?,
        })
    })?;

    let mut items = Vec::new();
    for row in rows {
        if let Ok(item) = row {
            items.push(item);
        }
    }
    Ok(items)
}

pub fn get_total_count(db_path: &str, query: &str) -> Result<i32> {
    let conn = Connection::open(db_path)?;
    let mut stmt = conn.prepare(
        "SELECT COUNT(*)
         FROM clipboard_items
         WHERE (?1 = '' OR summary LIKE ?2 OR search_text LIKE ?2);"
    )?;

    let pattern = format!("%{}%", query);
    let count: i32 = stmt.query_row(params![query, pattern], |row| row.get(0))?;
    Ok(count)
}

pub fn find_existing_by_hash(db_path: &str, hash: &str) -> Result<Option<ClipboardItem>> {
    let conn = Connection::open(db_path)?;
    let mut stmt = conn.prepare(
        "SELECT id, type, summary, search_text, text_content, html_content, rtf_content, image_path, file_list_json, created_at, expires_at, content_hash, display_metadata
         FROM clipboard_items
         WHERE content_hash = ?1
         LIMIT 1;"
    )?;

    let mut rows = stmt.query_map(params![hash], |row| {
        let files_json: Option<String> = row.get(8)?;
        let files: Option<Vec<FileClipboardEntry>> = files_json
            .and_then(|json| serde_json::from_str(&json).ok());

        Ok(ClipboardItem {
            id: row.get(0)?,
            item_type: ClipboardItemType::from_i32(row.get(1)?),
            summary: row.get(2)?,
            search_text: row.get(3)?,
            text_content: row.get(4)?,
            html_content: row.get(5)?,
            rtf_content: row.get(6)?,
            image_path: row.get(7)?,
            files,
            created_at: row.get(9)?,
            expires_at: row.get(10)?,
            content_hash: row.get(11)?,
            display_metadata: row.get(12)?,
        })
    })?;

    if let Some(Ok(item)) = rows.next() {
        Ok(Some(item))
    } else {
        Ok(None)
    }
}

pub fn add_or_update_item(db_path: &str, item: &ClipboardItem) -> Result<()> {
    let conn = Connection::open(db_path)?;
    let files_json = item.files.as_ref()
        .and_then(|f| serde_json::to_string(f).ok());

    conn.execute(
        "INSERT OR REPLACE INTO clipboard_items (
            id, type, summary, search_text, text_content, html_content, rtf_content, image_path, file_list_json, created_at, expires_at, content_hash, display_metadata
         ) VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9, ?10, ?11, ?12, ?13);",
        params![
            item.id,
            item.item_type.to_i32(),
            item.summary,
            item.search_text,
            item.text_content,
            item.html_content,
            item.rtf_content,
            item.image_path,
            files_json,
            item.created_at,
            item.expires_at,
            item.content_hash,
            item.display_metadata
        ],
    )?;
    Ok(())
}

pub fn delete_item(db_path: &str, id: &str) -> Result<()> {
    let conn = Connection::open(db_path)?;
    conn.execute("DELETE FROM clipboard_items WHERE id = ?1;", params![id])?;
    Ok(())
}

pub fn get_item_image_path(db_path: &str, id: &str) -> Result<Option<String>> {
    let conn = Connection::open(db_path)?;
    let mut stmt = conn.prepare("SELECT image_path FROM clipboard_items WHERE id = ?1 LIMIT 1;")?;
    let path: Option<String> = stmt.query_row(params![id], |row| row.get(0))?;
    Ok(path)
}

pub fn get_referenced_image_paths(db_path: &str) -> Result<Vec<String>> {
    let conn = Connection::open(db_path)?;
    let mut stmt = conn.prepare("SELECT image_path FROM clipboard_items WHERE image_path IS NOT NULL;")?;
    let rows = stmt.query_map([], |row| row.get(0))?;
    let mut paths = Vec::new();
    for row in rows {
        if let Ok(Some(path)) = row {
            paths.push(path);
        }
    }
    Ok(paths)
}

pub fn clear_all(db_path: &str) -> Result<()> {
    let conn = Connection::open(db_path)?;
    conn.execute("DELETE FROM clipboard_items;", [])?;
    Ok(())
}

pub fn delete_recent(db_path: &str, since: &str) -> Result<i32> {
    let conn = Connection::open(db_path)?;
    let deleted = conn.execute(
        "DELETE FROM clipboard_items WHERE datetime(created_at) >= datetime(?1);",
        params![since],
    )?;
    Ok(deleted as i32)
}

pub fn delete_latest(db_path: &str, count: i32) -> Result<i32> {
    if count == 0 {
        return Ok(0);
    }

    let conn = Connection::open(db_path)?;
    let sql = if count > 0 {
        "DELETE FROM clipboard_items WHERE id IN (
            SELECT id FROM clipboard_items ORDER BY datetime(created_at) DESC LIMIT ?1
         );"
    } else {
        "DELETE FROM clipboard_items WHERE id IN (
            SELECT id FROM clipboard_items ORDER BY datetime(created_at) ASC LIMIT ?1
         );"
    };

    let limit = if count > 0 { count } else { -count };
    let deleted = conn.execute(sql, params![limit])?;
    Ok(deleted as i32)
}

pub fn get_image_paths_for_latest(db_path: &str, count: i32) -> Result<Vec<String>> {
    if count == 0 {
        return Ok(Vec::new());
    }

    let conn = Connection::open(db_path)?;
    let sql = if count > 0 {
        "SELECT image_path FROM clipboard_items WHERE image_path IS NOT NULL AND id IN (
            SELECT id FROM clipboard_items ORDER BY datetime(created_at) DESC LIMIT ?1
         );"
    } else {
        "SELECT image_path FROM clipboard_items WHERE image_path IS NOT NULL AND id IN (
            SELECT id FROM clipboard_items ORDER BY datetime(created_at) ASC LIMIT ?1
         );"
    };

    let limit = if count > 0 { count } else { -count };
    let mut stmt = conn.prepare(sql)?;
    let rows = stmt.query_map(params![limit], |row| row.get(0))?;
    
    let mut paths = Vec::new();
    for row in rows {
        if let Ok(Some(path)) = row {
            paths.push(path);
        }
    }
    Ok(paths)
}

pub fn prune_expired(db_path: &str) -> Result<i32> {
    let conn = Connection::open(db_path)?;
    let deleted = conn.execute(
        "DELETE FROM clipboard_items WHERE expires_at IS NOT NULL AND datetime(expires_at) <= datetime('now');",
        [],
    )?;
    Ok(deleted as i32)
}

pub fn get_expired_image_paths(db_path: &str) -> Result<Vec<String>> {
    let conn = Connection::open(db_path)?;
    let mut stmt = conn.prepare(
        "SELECT image_path FROM clipboard_items WHERE image_path IS NOT NULL AND expires_at IS NOT NULL AND datetime(expires_at) <= datetime('now');"
    )?;
    let rows = stmt.query_map([], |row| row.get(0))?;
    
    let mut paths = Vec::new();
    for row in rows {
        if let Ok(Some(path)) = row {
            paths.push(path);
        }
    }
    Ok(paths)
}

pub fn enforce_max_items(db_path: &str, max_items: i32) -> Result<i32> {
    if max_items <= 0 {
        return Ok(0);
    }
    let conn = Connection::open(db_path)?;
    let deleted = conn.execute(
        "DELETE FROM clipboard_items WHERE id IN (
            SELECT id FROM clipboard_items ORDER BY datetime(created_at) DESC LIMIT -1 OFFSET ?1
         );",
        params![max_items],
    )?;
    Ok(deleted as i32)
}

pub fn get_pruned_image_paths_for_max(db_path: &str, max_items: i32) -> Result<Vec<String>> {
    if max_items <= 0 {
        return Ok(Vec::new());
    }
    let conn = Connection::open(db_path)?;
    let mut stmt = conn.prepare(
        "SELECT image_path FROM clipboard_items WHERE image_path IS NOT NULL AND id IN (
            SELECT id FROM clipboard_items ORDER BY datetime(created_at) DESC LIMIT -1 OFFSET ?1
         );"
    )?;
    let rows = stmt.query_map(params![max_items], |row| row.get(0))?;
    
    let mut paths = Vec::new();
    for row in rows {
        if let Ok(Some(path)) = row {
            paths.push(path);
        }
    }
    Ok(paths)
}

pub fn touch_item(db_path: &str, id: &str, timestamp: &str) -> Result<()> {
    let conn = Connection::open(db_path)?;
    conn.execute(
        "UPDATE clipboard_items SET created_at = ?2 WHERE id = ?1;",
        params![id, timestamp],
    )?;
    Ok(())
}
