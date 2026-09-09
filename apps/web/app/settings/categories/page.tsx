"use client";
import { useEffect,useState } from "react";
export default function Categories(){const [categories,setCategories]=useState<any[]>([]);useEffect(()=>{fetch("/api/categories").then(r=>r.ok?r.json():[]).then(setCategories)},[]);return <main><header><a href="/">← Work</a></header><h1>Categories</h1><p>Manage the categories available for new and edited work.</p><ul>{categories.map(c=><li key={c.id}>{c.name}{c.isArchived?" (archived)":""}</li>)}</ul></main>}
